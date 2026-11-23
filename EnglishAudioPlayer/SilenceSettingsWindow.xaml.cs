using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace AudioPausePlayer;

public partial class SilenceSettingsWindow : Window
{
    private readonly DispatcherTimer validationTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    public SilenceSettings Settings { get; private set; }

    public SilenceSettingsWindow(SilenceSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        LoadValues(settings);
        validationTimer.Tick += (_, _) => HideValueError();
        Closed += (_, _) => validationTimer.Stop();
    }

    private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new SilenceSettings();
        LoadValues(defaults);
        HideValueError();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadValues(out SilenceSettings settings)) return;
        Settings = settings;
        DialogResult = true;
    }

    private void LoadValues(SilenceSettings settings)
    {
        FrameMillisecondsTextBox.Text = settings.FrameMilliseconds.ToString(CultureInfo.InvariantCulture);
        MinimumSilenceTextBox.Text = settings.MinimumSilenceMilliseconds.ToString(CultureInfo.InvariantCulture);
        SilenceThresholdTextBox.Text = settings.SilenceThresholdDb.ToString(CultureInfo.InvariantCulture);
        NavigationToleranceTextBox.Text = settings.NavigationToleranceMilliseconds.ToString(CultureInfo.InvariantCulture);
        AllowedSilenceTextBox.Text = settings.AllowedSilenceSeconds.ToString("0.000", CultureInfo.InvariantCulture);
    }

    private bool TryReadValues(out SilenceSettings settings)
    {
        settings = new SilenceSettings();

        if (!int.TryParse(FrameMillisecondsTextBox.Text.Trim(), out int frameMilliseconds))
            return ShowValueError("分析帧长度必须是整数。");
        if (!int.TryParse(MinimumSilenceTextBox.Text.Trim(), out int minimumSilenceMilliseconds))
            return ShowValueError("最短停顿时长必须是整数。");
        if (!int.TryParse(SilenceThresholdTextBox.Text.Trim(), out int silenceThresholdDb))
            return ShowValueError("静音阈值必须是整数。");
        if (!int.TryParse(NavigationToleranceTextBox.Text.Trim(), out int navigationToleranceMilliseconds))
            return ShowValueError("前后跳转容差必须是整数。");
        string allowedText = AllowedSilenceTextBox.Text.Trim();
        if (!IsCompleteSilenceSeconds(allowedText)
            || !decimal.TryParse(allowedText, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal allowedSeconds))
            return ShowValueError("允许静音时长必须是完整的非负小数，最多三位小数。");

        settings.FrameMilliseconds = frameMilliseconds;
        settings.MinimumSilenceMilliseconds = minimumSilenceMilliseconds;
        settings.SilenceThresholdDb = silenceThresholdDb;
        settings.NavigationToleranceMilliseconds = navigationToleranceMilliseconds;
        settings.AllowedSilenceSeconds = allowedSeconds;

        try
        {
            settings.Validate();
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return ShowValueError(ex.Message.Split('\n')[0]);
        }

        return true;
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
        => Regex.IsMatch(text, @"^[0-9]*(?:\.[0-9]{0,3})?$");

    private static bool IsCompleteSilenceSeconds(string text)
        => Regex.IsMatch(text, @"^[0-9]+(?:\.[0-9]{1,3})?$");

    private void AllowedSilenceTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !IsEditableSilenceSeconds(BuildCandidateText((TextBox)sender, e.Text));

    private void AllowedSilenceTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text)
            || !IsEditableSilenceSeconds(BuildCandidateText((TextBox)sender,
                (string)e.DataObject.GetData(DataFormats.Text))))
            e.CancelCommand();
    }

    private void AllowedSilenceTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true; // Prevent the dialog's IsDefault OK button from closing it.
        ValidateLinkedSilenceValues();
    }

    private void MinimumSilenceTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        ValidateLinkedSilenceValues();
    }

    private void SilenceInput_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        => ValidateLinkedSilenceValues();

    private void ValidateLinkedSilenceValues()
    {
        if (!int.TryParse(MinimumSilenceTextBox.Text.Trim(), out int minimum)
            || minimum < 50 || minimum > 5000)
        {
            ShowValueError("最短停顿时长必须是 50 到 5000 ms 的整数。");
            return;
        }
        string input = AllowedSilenceTextBox.Text.Trim();
        if (!IsCompleteSilenceSeconds(input)
            || !decimal.TryParse(input, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out decimal allowed))
        {
            ShowValueError("允许静音时长不能为空，必须是最多三位小数的数值。");
            return;
        }
        if (allowed < minimum / 1000m)
        {
            ShowValueError($"允许静音时长不得低于 {minimum / 1000m:0.000} 秒。");
            return;
        }
        HideValueError();
    }

    private bool ShowValueError(string message)
    {
        validationTimer.Stop();
        ValidationText.Text = "参数错误：" + message;
        HelpText.Visibility = Visibility.Collapsed;
        ValidationBar.Visibility = Visibility.Visible;
        validationTimer.Start();
        return false;
    }

    private void HideValueError()
    {
        validationTimer.Stop();
        ValidationText.Text = "";
        ValidationBar.Visibility = Visibility.Collapsed;
        HelpText.Visibility = Visibility.Visible;
    }
}
