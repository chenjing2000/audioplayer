namespace AudioPlayer.Silence;

internal sealed record SilenceAnalysisSummary(int RangeCount, int CutCount, double RemovedSeconds, bool Saved);

// Called on one owning context; only file and PCM work runs in background tasks.
// Analysis and selection are independent because the selected track need not be playing.
internal sealed class SilenceTasks
{
    private readonly List<SilenceRange> ranges = new();
    private readonly List<Task> runningAnalyses = new();
    private readonly List<Task> runningSelectionChecks = new();
    private CancellationTokenSource? analysisCancellation;
    private CancellationTokenSource? selectionCancellation;
    private CancellationTokenSource? trimCancellation;
    private Task? trimTask;
    private bool closing;

    public bool IsAnalyzing { get; private set; }
    public bool AnalysisReady { get; private set; }
    public bool CanTrimSelected { get; private set; }
    public bool IsTrimming { get; private set; }
    public Task? AnalysisTask { get; private set; }
    public Task? SelectionTask { get; private set; }

    public event Action? StateChanged;
    public event Action? AnalysisCanceled;
    public event Action<string>? AnalysisStarted;
    public event Action<SilenceAnalysisSummary>? AnalysisCompleted;
    public event Action<Exception>? AnalysisFailed;
    public event Action? AnalysisFinished;

    public void CancelAnalysis()
    {
        analysisCancellation?.Cancel();
        AnalysisCanceled?.Invoke();
        IsAnalyzing = false;
        AnalysisReady = false;
        ranges.Clear();
    }

    public void StartAnalysis(string path, SilenceOptions settings)
    {
        if (closing) return;
        CancelAnalysis();
        var cancellation = new CancellationTokenSource();
        analysisCancellation = cancellation;
        IsAnalyzing = true;
        StateChanged?.Invoke();
        Task work = AnalyzeAsync(path, settings, cancellation);
        AnalysisTask = work;
        runningAnalyses.RemoveAll(task => task.IsCompleted);
        runningAnalyses.Add(work);
    }

    private bool IsCurrentAnalysis(CancellationTokenSource cancellation)
        => !closing && !cancellation.IsCancellationRequested
           && ReferenceEquals(analysisCancellation, cancellation);

    private async Task AnalyzeAsync(string path, SilenceOptions settings, CancellationTokenSource cancellation)
    {
        try
        {
            // Cache hits must not start a progress notification or run the detector.
            SilenceAnalysis? cached = await Task.Run(
                () => SilenceResultStore.TryLoadVerified(path, settings, cancellation.Token), cancellation.Token);
            if (!IsCurrentAnalysis(cancellation)) return;
            SilenceAnalysis result;
            if (cached is not null) result = cached;
            else
            {
                AnalysisStarted?.Invoke(path);
                result = await Task.Run(
                    () => SilenceResultStore.LoadOrDetect(path, settings, cancellation.Token), cancellation.Token);
            }
            if (!IsCurrentAnalysis(cancellation)) return;
            ranges.Clear();
            ranges.AddRange(result.Ranges);
            AnalysisReady = true;
            if (cached is null)
            {
                var cuts = result.Saved
                    ? SilenceTrimmer.PlanCuts(result.Ranges, result.Fingerprint.SampleRate, settings.AllowedSilenceSeconds)
                    : new List<SilenceCut>();
                double removed = cuts.Sum(c => c.CutEnd - c.CutStart) / (double)result.Fingerprint.SampleRate;
                AnalysisCompleted?.Invoke(new(ranges.Count, cuts.Count, removed, result.Saved));
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (IsCurrentAnalysis(cancellation)) AnalysisFailed?.Invoke(ex);
        }
        finally
        {
            if (IsCurrentAnalysis(cancellation))
            {
                IsAnalyzing = false;
                StateChanged?.Invoke();
                AnalysisFinished?.Invoke();
            }
            if (ReferenceEquals(analysisCancellation, cancellation)) analysisCancellation = null;
            cancellation.Dispose();
        }
    }

    public void CancelSelectionCheck()
    {
        selectionCancellation?.Cancel();
        CanTrimSelected = false;
    }

    public void CheckTrimAvailability(string? path, SilenceOptions settings)
    {
        CancelSelectionCheck();
        if (closing) return;
        StateChanged?.Invoke();
        if (path is null) return;
        var cancellation = new CancellationTokenSource();
        selectionCancellation = cancellation;
        Task work = CheckSelectedCacheAsync(path, settings, cancellation);
        SelectionTask = work;
        runningSelectionChecks.RemoveAll(task => task.IsCompleted);
        runningSelectionChecks.Add(work);
    }

    private async Task CheckSelectedCacheAsync(string path, SilenceOptions settings, CancellationTokenSource cancellation)
    {
        try
        {
            bool available = await Task.Run(() =>
            {
                SilenceAnalysis? cached = SilenceResultStore.TryLoadVerified(path, settings, cancellation.Token);
                return cached is not null && SilenceTrimmer.PlanCuts(
                    cached.Ranges, cached.Fingerprint.SampleRate, settings.AllowedSilenceSeconds).Count > 0;
            }, cancellation.Token);
            if (!closing && !cancellation.IsCancellationRequested && ReferenceEquals(selectionCancellation, cancellation))
            {
                CanTrimSelected = available;
                StateChanged?.Invoke();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception) { /* Unreadable caches cannot enable trimming. */ }
        finally
        {
            if (ReferenceEquals(selectionCancellation, cancellation)) selectionCancellation = null;
            cancellation.Dispose();
        }
    }

    public TimeSpan? FindNext(TimeSpan current, int tolerance)
        => AnalysisReady ? SilenceDetector.FindNext(ranges, current, tolerance) : null;

    public TimeSpan? FindPrevious(TimeSpan current, int tolerance)
        => AnalysisReady ? SilenceDetector.FindPrevious(ranges, current, tolerance) : null;

    // Keep trimming active until the caller commits the output to the current
    // playlist; that commit may need to await an overlapping folder switch.
    public Task TrimAsync(string path, SilenceOptions settings, Func<SilenceTrimResult, Task> commit)
    {
        if (closing || IsTrimming || IsAnalyzing || !CanTrimSelected)
            throw new InvalidOperationException("当前不能裁剪音频。");
        var cancellation = new CancellationTokenSource();
        trimCancellation = cancellation;
        IsTrimming = true;
        StateChanged?.Invoke();
        return trimTask = RunTrimAsync(path, settings, commit, cancellation);
    }

    private async Task RunTrimAsync(string path, SilenceOptions settings,
        Func<SilenceTrimResult, Task> commit, CancellationTokenSource cancellation)
    {
        try
        {
            SilenceTrimResult result = await SilenceTrimmer.TrimAsync(path, settings, cancellation.Token);
            if (!closing) await commit(result);
        }
        finally
        {
            trimCancellation = null;
            cancellation.Dispose();
            IsTrimming = false;
            if (!closing) StateChanged?.Invoke();
        }
    }

    public void CancelPending()
    {
        closing = true;
        CancelAnalysis();
        CancelSelectionCheck();
        trimCancellation?.Cancel();
    }

    public async Task WaitForPendingAsync()
    {
        await Task.WhenAll(runningAnalyses);
        await Task.WhenAll(runningSelectionChecks);
        if (trimTask is not null)
        {
            try { await trimTask; }
            catch (OperationCanceledException) { }
            catch (Exception) { /* The initiating caller handles trim errors. */ }
        }
    }
}
