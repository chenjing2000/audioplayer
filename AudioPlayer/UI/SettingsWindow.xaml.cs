using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

using AudioPlayer.Playback;
using AudioPlayer.Script;
using AudioPlayer.Settings;
using AudioPlayer.Silence;

namespace AudioPlayer.UI;

public partial class SettingsWindow : Window
{
    private TextBox? invalidField;
    private PlaybackMode playbackMode;
    public AppSettings Settings { get; private set; }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        Settings = settings;
        FontBox.ItemsSource = ScriptAppearance.Fonts;
        TextColorBox.ItemsSource = ScriptAppearance.Colors;
        PlaybackColorBox.ItemsSource = ScriptAppearance.PlaybackColors;
        BackgroundColorBox.ItemsSource = ScriptAppearance.Backgrounds;
        LoadValues(settings);

    }

    private void RestoreDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        var defaults = new AppSettings();
        LoadValues(defaults);
        ClearValueError();
        ToastMessages.Clear();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadValues(out AppSettings settings)) return;
        Settings = settings;
        DialogResult = true;
    }

    private void LoadValues(AppSettings settings)
    {
        var appearance = settings.ScriptAppearance;
        FontBox.SelectedItem = appearance.Font;
        FontSizeBox.Text = appearance.FontSize.ToString(CultureInfo.InvariantCulture);
        LineHeightBox.Text = appearance.LineHeight.ToString("0.##", CultureInfo.InvariantCulture);
        ParagraphAfterBox.Text = appearance.ParagraphAfter.ToString(CultureInfo.InvariantCulture);
        IndentBox.Text = appearance.FirstLineIndent.ToString(CultureInfo.InvariantCulture);
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

    private bool TryReadValues(out AppSettings settings)
    {
        settings = new AppSettings { PlaybackMode = playbackMode };

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
        foreach (var (field, message) in new[]
        {
            (FontSizeBox, $"字号必须是 {ScriptAppearance.MinFontSize} 到 {ScriptAppearance.MaxFontSize} 的整数。"),
            (LineHeightBox, $"行高必须在 {ScriptAppearance.MinLineHeight} 到 {ScriptAppearance.MaxLineHeight} 之间，且最多两位小数。"),
            (ParagraphAfterBox, $"段后间距必须是 {ScriptAppearance.MinParagraphAfter} 到 {ScriptAppearance.MaxParagraphAfter} 的整数。"),
            (IndentBox, $"首行缩进必须是 {ScriptAppearance.MinFirstLineIndent} 到 {ScriptAppearance.MaxFirstLineIndent} 的整数。")
        })
            if (!IsFieldValid(field)) return ShowValueError(message, field);

        settings.ScriptAppearance = ReadAppearance();
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
                nameof(AppSettings.FrameMilliseconds) => FrameMillisecondsTextBox,
                nameof(AppSettings.MinimumSilenceMilliseconds) => MinimumSilenceTextBox,
                nameof(AppSettings.SilenceThresholdDb) => SilenceThresholdTextBox,
                nameof(AppSettings.NavigationToleranceMilliseconds) => NavigationToleranceTextBox,
                nameof(AppSettings.BackwardStepSeconds) => BackwardStepTextBox,
                nameof(AppSettings.ForwardStepSeconds) => ForwardStepTextBox,
                nameof(AppSettings.ExcludedFolderNames) => ExcludedFolderNamesTextBox,
                _ => AllowedSilenceTextBox
            };
            return ShowValueError(ex.Message.Split('\n')[0], field);
        }

        settings.ExcludedFolderNames = string.Join(", ", excludedNames);
        ExcludedFolderNamesTextBox.Text = settings.ExcludedFolderNames;
        BackwardStepTextBox.Text = backward.ToString(CultureInfo.InvariantCulture);
        ForwardStepTextBox.Text = forward.ToString(CultureInfo.InvariantCulture);
        AllowedSilenceTextBox.Text = allowedSeconds.ToString("0.0", CultureInfo.InvariantCulture);
        FontSizeBox.Text = settings.ScriptAppearance.FontSize.ToString(CultureInfo.InvariantCulture);
        LineHeightBox.Text = settings.ScriptAppearance.LineHeight.ToString("0.##", CultureInfo.InvariantCulture);
        ParagraphAfterBox.Text = settings.ScriptAppearance.ParagraphAfter.ToString(CultureInfo.InvariantCulture);
        IndentBox.Text = settings.ScriptAppearance.FirstLineIndent.ToString(CultureInfo.InvariantCulture);
        ClearValueError();
        return true;
    }

    // WPF single-selection ListBox can also deselect via Ctrl+click/Space.
    // Restore the removed color without maintaining a second selection state.
    private void ColorSelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox list && list.SelectedIndex < 0 && list.Items.Count > 0)
        {
            if (e.RemovedItems.Count > 0 && list.Items.Contains(e.RemovedItems[0]))
                list.SelectedItem = e.RemovedItems[0];
            else
                list.SelectedIndex = 0;
        }
    }

    // Called after numeric fields have passed validation; defaults and ranges live in ScriptAppearance.
    private ScriptAppearance ReadAppearance() => new()
    {
        Font = (string)FontBox.SelectedItem,
        FontSize = int.Parse(FontSizeBox.Text, NumberStyles.None, CultureInfo.InvariantCulture),
        LineHeight = double.Parse(LineHeightBox.Text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
        ParagraphAfter = int.Parse(ParagraphAfterBox.Text, NumberStyles.None, CultureInfo.InvariantCulture),
        FirstLineIndent = int.Parse(IndentBox.Text, NumberStyles.None, CultureInfo.InvariantCulture),
        Color = (string)TextColorBox.SelectedItem,
        ScriptColor = (string)PlaybackColorBox.SelectedItem,
        Background = (string)BackgroundColorBox.SelectedItem
    };

    private static bool IsStepText(string text) => text.Length <= 2 && text.All(c => c is >= '0' and <= '9');

    private void StepTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        var box = (TextBox)sender;
        string candidate = BuildCandidateText(box, e.Text);
        e.Handled = !IsStepText(candidate) || candidate.Length > box.MaxLength;
    }

    private void StepTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        var box = (TextBox)sender;
        ValidatePaste(box, e, text => IsStepText(text) && text.Length <= box.MaxLength);
    }

    private static bool IsLineHeightText(string text)
        => Regex.IsMatch(text, @"^[0-9]?(?:\.[0-9]{0,2})?\z");

    private void LineHeightBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !IsLineHeightText(BuildCandidateText((TextBox)sender, e.Text));

    private void LineHeightBox_Pasting(object sender, DataObjectPastingEventArgs e)
        => ValidatePaste((TextBox)sender, e, IsLineHeightText);

    private void PositiveIntegerTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !IsIntegerText(BuildCandidateText((TextBox)sender, e.Text), false);

    private void SignedIntegerTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        => e.Handled = !IsIntegerText(BuildCandidateText((TextBox)sender, e.Text), true);

    private void PositiveIntegerTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        => ValidatePaste((TextBox)sender, e, text => IsIntegerText(text, false));

    private void SignedIntegerTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        => ValidatePaste((TextBox)sender, e, text => IsIntegerText(text, true));

    private static void ValidatePaste(TextBox textBox, DataObjectPastingEventArgs e, Func<string, bool> isValid)
    {
        if (!e.DataObject.GetDataPresent(DataFormats.Text)
            || !isValid(BuildCandidateText(textBox, (string)e.DataObject.GetData(DataFormats.Text))))
            e.CancelCommand();
    }

    private static string BuildCandidateText(TextBox textBox, string input)
    {
        string current = textBox.Text ?? string.Empty;
        return current.Remove(textBox.SelectionStart, textBox.SelectionLength)
                      .Insert(textBox.SelectionStart, input);
    }

    private static bool IsIntegerText(string text, bool allowNegative)
    {
        if (text.Length == 0) return true;

        int start = text[0] == '+' || (allowNegative && text[0] == '-') ? 1 : 0;
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
        => ValidatePaste((TextBox)sender, e, IsEditableSilenceSeconds);

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (FontBox.IsKeyboardFocusWithin || TextColorBox.IsKeyboardFocusWithin ||
            PlaybackColorBox.IsKeyboardFocusWithin || BackgroundColorBox.IsKeyboardFocusWithin) return;
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
            try { new AppSettings { ExcludedFolderNames = box.Text }.GetExcludedFolderNames(); return true; }
            catch (ArgumentOutOfRangeException) { return false; }
        }
        if (ReferenceEquals(box, BackwardStepTextBox) || ReferenceEquals(box, ForwardStepTextBox))
            return box.Text.Length > 0 && IsStepText(box.Text);
        if (ReferenceEquals(box, FontSizeBox))
            return IsStepText(box.Text) && int.TryParse(box.Text, out int fontSize) &&
                fontSize is >= ScriptAppearance.MinFontSize and <= ScriptAppearance.MaxFontSize;
        if (ReferenceEquals(box, LineHeightBox))
            return IsLineHeightText(box.Text) && double.TryParse(box.Text, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out double lineHeight) &&
                lineHeight is >= ScriptAppearance.MinLineHeight and <= ScriptAppearance.MaxLineHeight;
        if (ReferenceEquals(box, ParagraphAfterBox))
            return IsStepText(box.Text) && int.TryParse(box.Text, out int paragraphAfter) &&
                paragraphAfter is >= ScriptAppearance.MinParagraphAfter and <= ScriptAppearance.MaxParagraphAfter;
        if (ReferenceEquals(box, IndentBox))
            return box.Text.Length == 1 && IsStepText(box.Text) && int.TryParse(box.Text, out int indent) &&
                indent is >= ScriptAppearance.MinFirstLineIndent and <= ScriptAppearance.MaxFirstLineIndent;
        if (ReferenceEquals(box, FrameMillisecondsTextBox))
            return int.TryParse(box.Text, out int frameValue) && frameValue is >= SilenceOptions.MinFrameMilliseconds and <= SilenceOptions.MaxFrameMilliseconds;
        if (ReferenceEquals(box, MinimumSilenceTextBox))
            return int.TryParse(box.Text, out int minimumValue) && minimumValue is >= SilenceOptions.MinMinimumSilenceMilliseconds and <= SilenceOptions.MaxMinimumSilenceMilliseconds;
        if (ReferenceEquals(box, SilenceThresholdTextBox))
            return int.TryParse(box.Text, out int thresholdValue) && thresholdValue is >= SilenceOptions.MinSilenceThresholdDb and <= SilenceOptions.MaxSilenceThresholdDb;
        if (ReferenceEquals(box, NavigationToleranceTextBox))
            return int.TryParse(box.Text, out int toleranceValue) && toleranceValue is >= SilenceOptions.MinNavigationToleranceMilliseconds and <= SilenceOptions.MaxNavigationToleranceMilliseconds;
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
