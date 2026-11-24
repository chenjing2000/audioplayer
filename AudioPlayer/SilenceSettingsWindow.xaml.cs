using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AudioPlayer;

public partial class SilenceSettingsWindow : Window
{
    private TextBox? invalidField;
    private PlaybackMode playbackMode;
    public SilenceSettings Settings { get; private set; }

    public SilenceSettingsWindow(SilenceSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        FontBox.ItemsSource = ScriptAppearance.Fonts;
        FontSizeBox.ItemsSource = ScriptAppearance.Sizes;
        LineHeightBox.ItemsSource = ScriptAppearance.Heights;
        ParagraphAfterBox.ItemsSource = ScriptAppearance.Spacings;
        IndentBox.ItemsSource = ScriptAppearance.Indents;
        TextColorBox.ItemsSource = ScriptAppearance.Colors;
        PlaybackColorBox.ItemsSource = ScriptAppearance.PlaybackColors;
        BackgroundColorBox.ItemsSource = ScriptAppearance.Backgrounds;
        LoadValues(settings);

    }

    private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new SilenceSettings();
        LoadValues(defaults);
        ClearValueError();
        ToastMessages.Clear();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadValues(out SilenceSettings settings)) return;
        Settings = settings;
        DialogResult = true;
    }

    private void LoadValues(SilenceSettings settings)
    {
        var appearance = settings.ScriptAppearance;
        FontBox.SelectedItem = appearance.Font;
        FontSizeBox.SelectedItem = appearance.FontSize;
        LineHeightBox.SelectedItem = appearance.LineHeight;
        ParagraphAfterBox.SelectedItem = appearance.ParagraphAfter;
        IndentBox.SelectedItem = appearance.FirstLineIndent;
        TextColorBox.SelectedItem = appearance.Color;
        PlaybackColorBox.SelectedItem = appearance.ScriptColor;
        BackgroundColorBox.SelectedItem = appearance.Background;
        playbackMode = settings.PlaybackMode;
        ExcludedFolderNamesTextBox.Text = settings.ExcludedFolderNames;
        BackwardStepTextBox.Text = settings.BackwardStepSeconds.ToString(CultureInfo.InvariantCulture);
        ForwardStepTextBox.Text = settings.ForwardStepSeconds.ToString(CultureInfo.InvariantCulture);
        FrameMillisecondsTextBox.Text = settings.FrameMilliseconds.ToString(CultureInfo.InvariantCulture);
        MinimumSilenceTextBox.Text = settings.MinimumSilenceMilliseconds.ToString(CultureInfo.InvariantCulture);
        SilenceThresholdTextBox.Text = settings.SilenceThresholdDb.ToString(CultureInfo.InvariantCulture);
        NavigationToleranceTextBox.Text = settings.NavigationToleranceMilliseconds.ToString(CultureInfo.InvariantCulture);
        AllowedSilenceTextBox.Text = settings.AllowedSilenceSeconds.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private bool TryReadValues(out SilenceSettings settings)
    {
        settings = new SilenceSettings { PlaybackMode = playbackMode, ScriptAppearance = ReadAppearance() };

        if (!int.TryParse(FrameMillisecondsTextBox.Text.Trim(), out int frameMilliseconds))
            return ShowValueError("分析帧长度必须是整数。", FrameMillisecondsTextBox);
        if (!int.TryParse(MinimumSilenceTextBox.Text.Trim(), out int minimumSilenceMilliseconds))
            return ShowValueError("最短停顿时长必须是整数。", MinimumSilenceTextBox);
        if (!int.TryParse(SilenceThresholdTextBox.Text.Trim(), out int silenceThresholdDb))
            return ShowValueError("静音阈值必须是整数。", SilenceThresholdTextBox);
        if (!int.TryParse(NavigationToleranceTextBox.Text.Trim(), out int navigationToleranceMilliseconds))
            return ShowValueError("前后跳转容差必须是整数。", NavigationToleranceTextBox);
        string allowedText = AllowedSilenceTextBox.Text.Trim();
        if (!IsCompleteSilenceSeconds(allowedText)
            || !decimal.TryParse(allowedText, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal allowedSeconds))
            return ShowValueError("允许静音时长不能为空，且最多只能有一位小数。", AllowedSilenceTextBox);

        if (!IsStepText(BackwardStepTextBox.Text) || !int.TryParse(BackwardStepTextBox.Text, out int backward))
            return ShowValueError("后退步长必须是 0 到 99 的整数。", BackwardStepTextBox);
        if (!IsStepText(ForwardStepTextBox.Text) || !int.TryParse(ForwardStepTextBox.Text, out int forward))
            return ShowValueError("前进步长必须是 0 到 99 的整数。", ForwardStepTextBox);
        settings.ExcludedFolderNames = ExcludedFolderNamesTextBox.Text;
        settings.BackwardStepSeconds = backward;
        settings.ForwardStepSeconds = forward;
        settings.FrameMilliseconds = frameMilliseconds;
        settings.MinimumSilenceMilliseconds = minimumSilenceMilliseconds;
        settings.SilenceThresholdDb = silenceThresholdDb;
        settings.NavigationToleranceMilliseconds = navigationToleranceMilliseconds;
        settings.AllowedSilenceSeconds = allowedSeconds;

        HashSet<string> excludedNames;
        try
        {
            excludedNames = settings.ValidateAndGetExcludedFolderNames();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            TextBox field = ex.ParamName switch
            {
                nameof(SilenceSettings.FrameMilliseconds) => FrameMillisecondsTextBox,
                nameof(SilenceSettings.MinimumSilenceMilliseconds) => MinimumSilenceTextBox,
                nameof(SilenceSettings.SilenceThresholdDb) => SilenceThresholdTextBox,
                nameof(SilenceSettings.NavigationToleranceMilliseconds) => NavigationToleranceTextBox,
                nameof(SilenceSettings.BackwardStepSeconds) => BackwardStepTextBox,
                nameof(SilenceSettings.ForwardStepSeconds) => ForwardStepTextBox,
                nameof(SilenceSettings.ExcludedFolderNames) => ExcludedFolderNamesTextBox,
                _ => AllowedSilenceTextBox
            };
            return ShowValueError(ex.Message.Split('\n')[0], field);
        }

        settings.ExcludedFolderNames = string.Join(", ", excludedNames);
        ExcludedFolderNamesTextBox.Text = settings.ExcludedFolderNames;
        BackwardStepTextBox.Text = backward.ToString(CultureInfo.InvariantCulture);
        ForwardStepTextBox.Text = forward.ToString(CultureInfo.InvariantCulture);
        AllowedSilenceTextBox.Text = allowedSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        ClearValueError();
        return true;
    }

    private ScriptAppearance ReadAppearance() => new()
    {
        Font = (string?)FontBox?.SelectedItem ?? "Georgia",
        FontSize = (int?)FontSizeBox?.SelectedItem ?? 18,
        LineHeight = (double?)LineHeightBox?.SelectedItem ?? 1.7,
        ParagraphAfter = (int?)ParagraphAfterBox?.SelectedItem ?? 16,
        FirstLineIndent = (int?)IndentBox?.SelectedItem ?? 0,
        Color = (string?)TextColorBox?.SelectedItem ?? "#283747",
        ScriptColor = (string?)PlaybackColorBox?.SelectedItem ?? "#B64550",
        Background = (string?)BackgroundColorBox?.SelectedItem ?? "#F8F8F6"
    };

    private void Appearance_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (PreviewSecond is null || PreviewPlayed is null) return;
        var a = ReadAppearance();
        Brush BrushFor(string color) => (Brush)new BrushConverter().ConvertFromString(color)!;
        AppearancePreview.Background = BrushFor(a.Background);
        foreach (var block in new[] { PreviewFirst, PreviewSecond })
        {
            block.FontFamily = new FontFamily(a.Font + ", Segoe UI");
            block.FontSize = a.FontSize;
            block.LineHeight = a.FontSize * a.LineHeight;
            block.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            block.Foreground = BrushFor(a.Color);

        }
        PreviewPlayed.Text = new string('\u2003', a.FirstLineIndent) + "Learning changes ";
        PreviewSecond.Text = new string('\u2003', a.FirstLineIndent) + "Each experience shapes our understanding.";
        PreviewFirst.Margin = new Thickness(0, 0, 0, a.ParagraphAfter);
        PreviewPlayed.Foreground = BrushFor(a.ScriptColor);
    }

    private static bool IsStepText(string text) => text.Length <= 2 && text.All(c => c is >= '0' and <= '9');

    private void StepTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !IsStepText(BuildCandidateText((TextBox)sender, e.Text));

    private void StepTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text)
            || !IsStepText(BuildCandidateText((TextBox)sender, (string)e.DataObject.GetData(DataFormats.Text))))
            e.CancelCommand();
    }

    private void PositiveIntegerTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var textBox = (TextBox)sender;
        string candidate = BuildCandidateText(textBox, e.Text);
        e.Handled = !IsPositiveIntegerText(candidate);
    }

    private void SignedIntegerTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var textBox = (TextBox)sender;
        string candidate = BuildCandidateText(textBox, e.Text);
        e.Handled = !IsSignedIntegerText(candidate);
    }

    private void PositiveIntegerTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        ValidatePaste((TextBox)sender, e, false);
    }

    private void SignedIntegerTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        ValidatePaste((TextBox)sender, e, true);
    }

    private static void ValidatePaste(TextBox textBox, DataObjectPastingEventArgs e, bool allowNegative)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text))
        {
            e.CancelCommand();
            return;
        }

        string pastedText = (string)e.DataObject.GetData(DataFormats.Text);
        string candidate = BuildCandidateText(textBox, pastedText);
        bool valid = allowNegative ? IsSignedIntegerText(candidate) : IsPositiveIntegerText(candidate);
        if (!valid) e.CancelCommand();
    }

    private static string BuildCandidateText(TextBox textBox, string input)
    {
        string current = textBox.Text ?? string.Empty;
        return current.Remove(textBox.SelectionStart, textBox.SelectionLength)
                      .Insert(textBox.SelectionStart, input);
    }

    private static bool IsPositiveIntegerText(string text)
    {
        if (text.Length == 0) return true;

        int start = text[0] == '+' ? 1 : 0;
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] < '0' || text[i] > '9') return false;
        }
        return true;
    }

    private static bool IsSignedIntegerText(string text)
    {
        if (text.Length == 0) return true;

        int start = text[0] == '+' || text[0] == '-' ? 1 : 0;
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] < '0' || text[i] > '9') return false;
        }
        return true;
    }

    private static bool IsEditableSilenceSeconds(string text)
        => Regex.IsMatch(text, @"^[0-9]*(?:\.[0-9]?)?$");

    private static bool IsCompleteSilenceSeconds(string text)
        => Regex.IsMatch(text, @"^[0-9]+(?:\.[0-9]?)?$");

    private void AllowedSilenceTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !IsEditableSilenceSeconds(BuildCandidateText((TextBox)sender, e.Text));

    private void AllowedSilenceTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text)
            || !IsEditableSilenceSeconds(BuildCandidateText((TextBox)sender,
                (string)e.DataObject.GetData(DataFormats.Text))))
            e.CancelCommand();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true; // Enter checks values without accepting or closing the dialog.
        TryReadValues(out _);
    }

    private void SilenceInput_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Dialog action buttons own their own handling. Avoid validating twice on a click.
        if (ReferenceEquals(e.NewFocus, OkButton)
            || ReferenceEquals(e.NewFocus, CancelButton)
            || ReferenceEquals(e.NewFocus, RestoreDefaultsButton))
            return;
        TryReadValues(out _);
    }

    private void Field_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box || !ReferenceEquals(box, invalidField)) return;
        if (IsFieldValid(box)) ClearValueError();
    }

    private bool IsFieldValid(TextBox box)
    {
        if (ReferenceEquals(box, ExcludedFolderNamesTextBox))
        {
            try { new SilenceSettings { ExcludedFolderNames = box.Text }.GetExcludedFolderNames(); return true; }
            catch (ArgumentOutOfRangeException) { return false; }
        }
        if (ReferenceEquals(box, BackwardStepTextBox) || ReferenceEquals(box, ForwardStepTextBox))
            return box.Text.Length > 0 && IsStepText(box.Text);
        if (ReferenceEquals(box, FrameMillisecondsTextBox))
            return int.TryParse(box.Text, out int frameValue) && frameValue is >= 1 and <= 100;
        if (ReferenceEquals(box, MinimumSilenceTextBox))
            return int.TryParse(box.Text, out int minimumValue) && minimumValue is >= 50 and <= 5000;
        if (ReferenceEquals(box, SilenceThresholdTextBox))
            return int.TryParse(box.Text, out int thresholdValue) && thresholdValue is >= -100 and <= 0;
        if (ReferenceEquals(box, NavigationToleranceTextBox))
            return int.TryParse(box.Text, out int toleranceValue) && toleranceValue is >= 1 and <= 5000;
        string input = box.Text.Trim();
        return IsCompleteSilenceSeconds(input)
            && decimal.TryParse(input, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal seconds)
            && int.TryParse(MinimumSilenceTextBox.Text, out int minimum)
            && seconds >= minimum / 1000m;
    }

    private bool ShowValueError(string message, TextBox field)
    {
        if (invalidField is not null && !ReferenceEquals(invalidField, field))
            invalidField.ClearValue(TextBox.BorderBrushProperty);
        invalidField = field;
        field.BorderBrush = Brushes.IndianRed;
        ToastMessages.Show(message, ToastLevel.Warning);
        return false;
    }

    private void ClearValueError()
    {
        invalidField?.ClearValue(TextBox.BorderBrushProperty);
        invalidField = null;
    }
}
