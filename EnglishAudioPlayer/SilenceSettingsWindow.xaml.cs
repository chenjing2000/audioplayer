using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AudioPausePlayer;

public partial class SilenceSettingsWindow : Window
{
    private TextBox? invalidField;
    public SilenceSettings Settings { get; private set; }

    public SilenceSettingsWindow(SilenceSettings settings)
    {
        InitializeComponent();
        Settings = settings;
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
        FrameMillisecondsTextBox.Text = settings.FrameMilliseconds.ToString(CultureInfo.InvariantCulture);
        MinimumSilenceTextBox.Text = settings.MinimumSilenceMilliseconds.ToString(CultureInfo.InvariantCulture);
        SilenceThresholdTextBox.Text = settings.SilenceThresholdDb.ToString(CultureInfo.InvariantCulture);
        NavigationToleranceTextBox.Text = settings.NavigationToleranceMilliseconds.ToString(CultureInfo.InvariantCulture);
        AllowedSilenceTextBox.Text = settings.AllowedSilenceSeconds.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private bool TryReadValues(out SilenceSettings settings)
    {
        settings = new SilenceSettings();

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
            TextBox field = ex.ParamName switch
            {
                nameof(SilenceSettings.FrameMilliseconds) => FrameMillisecondsTextBox,
                nameof(SilenceSettings.MinimumSilenceMilliseconds) => MinimumSilenceTextBox,
                nameof(SilenceSettings.SilenceThresholdDb) => SilenceThresholdTextBox,
                nameof(SilenceSettings.NavigationToleranceMilliseconds) => NavigationToleranceTextBox,
                _ => AllowedSilenceTextBox
            };
            return ShowValueError(ex.Message.Split('\n')[0], field);
        }

        AllowedSilenceTextBox.Text = allowedSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        ClearValueError();
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
        => TryReadValues(out _);

    private void Field_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box || !ReferenceEquals(box, invalidField)) return;
        if (IsFieldValid(box)) ClearValueError();
    }

    private bool IsFieldValid(TextBox box)
    {
        if (ReferenceEquals(box, FrameMillisecondsTextBox))
            return int.TryParse(box.Text, out int value) && value is >= 1 and <= 100;
        if (ReferenceEquals(box, MinimumSilenceTextBox))
            return int.TryParse(box.Text, out int value) && value is >= 50 and <= 5000;
        if (ReferenceEquals(box, SilenceThresholdTextBox))
            return int.TryParse(box.Text, out int value) && value is >= -100 and <= 0;
        if (ReferenceEquals(box, NavigationToleranceTextBox))
            return int.TryParse(box.Text, out int value) && value is >= 1 and <= 5000;
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
