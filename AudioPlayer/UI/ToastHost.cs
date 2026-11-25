using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AudioPlayer.UI;

internal enum ToastLevel { Info, Warning, Error }

// Shared WPF-only overlay for the player and its modal settings dialog.
public sealed class ToastHost : Grid
{
    internal sealed class ToastMessage
    {
        internal required Border View { get; init; }
        internal required TextBlock Text { get; init; }
        internal required Button CloseButton { get; init; }
        internal required DispatcherTimer Timer { get; init; }
        internal ToastLevel Level;
        internal bool Task;
        internal bool Closing;
        internal DateTime StartedUtc;
        internal TimeSpan Remaining;
    }

    private readonly StackPanel stack = new() { Orientation = Orientation.Vertical };
    private readonly Queue<ToastMessage> pending = new();
    private readonly List<ToastMessage> visible = new();

    public ToastHost()
    {
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Bottom;
        MaxWidth = 460;
        IsHitTestVisible = true;
        Children.Add(stack);
    }

    internal void Show(string message, ToastLevel level, TimeSpan? lifetime = null)
    {
        var item = Create(message, level, false);
        item.Remaining = lifetime ?? TimeSpan.FromSeconds(5);
        ShowOrQueue(item);
    }

    internal ToastMessage ShowTask(string message)
    {
        var item = Create(message, ToastLevel.Info, true);
        // A task must be visible even when short-lived messages have filled the overlay.
        if (visible.Count >= 3)
        {
            ToastMessage? displaced = visible.FirstOrDefault(x => !x.Task);
            if (displaced is not null)
            {
                RemoveNow(displaced);
                var rest = pending.ToArray();
                pending.Clear();
                pending.Enqueue(displaced);
                foreach (var old in rest) pending.Enqueue(old);
            }
        }
        ShowOrQueue(item);
        return item;
    }

    internal void CompleteTask(ToastMessage item, string message, ToastLevel level,
        TimeSpan? lifetime = null)
    {
        if (item.Closing) return;
        item.Task = false;
        item.Level = level;
        item.Text.Text = Prefix(level) + message;
        ApplyColor(item);
        item.CloseButton.Visibility = Visibility.Visible;
        item.Remaining = lifetime ?? TimeSpan.FromSeconds(6);
        StartTimer(item);
    }

    internal void CancelTask(ToastMessage item)
    {
        if (!item.Task) return;
        item.Task = false;
        RemoveNow(item);
        if (pending.Count > 0 && visible.Count < 3) ShowOrQueue(pending.Dequeue());
    }

    internal void Clear()
    {
        foreach (var item in visible)
        {
            item.Timer.Stop();
            item.View.BeginAnimation(OpacityProperty, null);
        }
        visible.Clear();
        pending.Clear();
        stack.Children.Clear();
    }

    private ToastMessage Create(string text, ToastLevel level, bool task)
    {
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = Prefix(level) + text,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 58,
            LineHeight = 18,
            VerticalAlignment = VerticalAlignment.Center
        };
        content.Children.Add(label);

        var close = new Button
        {
            Content = "×",
            Width = 24,
            Height = 24,
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Focusable = false,
            Visibility = task ? Visibility.Collapsed : Visibility.Visible,
            ToolTip = "关闭消息"
        };
        Grid.SetColumn(close, 1);
        content.Children.Add(close);

        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 9, 8, 9),
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Child = content,
            Opacity = 0
        };
        var timer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(5) };
        var item = new ToastMessage { View = border, Text = label, CloseButton = close, Timer = timer,
            Level = level, Task = task, Remaining = TimeSpan.FromSeconds(5) };
        ApplyColor(item);
        close.Click += (_, _) => Dismiss(item);
        border.MouseEnter += (_, _) =>
        {
            if (!item.Timer.IsEnabled) return;
            item.Timer.Stop();
            item.Remaining -= DateTime.UtcNow - item.StartedUtc;
            if (item.Remaining < TimeSpan.FromMilliseconds(1))
                item.Remaining = TimeSpan.FromMilliseconds(1);
        };
        border.MouseLeave += (_, _) => StartTimer(item);
        timer.Tick += (_, _) => Dismiss(item);
        return item;
    }

    private static string Prefix(ToastLevel level) => level switch
    {
        ToastLevel.Info => "普通：",
        ToastLevel.Warning => "警告：",
        _ => "错误："
    };

    private static void ApplyColor(ToastMessage item)
    {
        var (background, border, foreground) = item.Level switch
        {
            ToastLevel.Info => ("#E8F2FF", "#9EC5F4", "#174A80"),
            ToastLevel.Warning => ("#FFF4D6", "#E9C46B", "#92400E"),
            _ => ("#FDE8E8", "#F0A3A3", "#991B1B")
        };
        item.View.Background = (Brush)new BrushConverter().ConvertFromString(background)!;
        item.View.BorderBrush = (Brush)new BrushConverter().ConvertFromString(border)!;
        item.Text.Foreground = (Brush)new BrushConverter().ConvertFromString(foreground)!;
    }

    private void ShowOrQueue(ToastMessage item)
    {
        if (visible.Count == 3)
        {
            pending.Enqueue(item);
            return;
        }
        visible.Add(item);
        stack.Children.Insert(0, item.View); // The newest visible message sits at the top.
        item.View.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
        { FillBehavior = FillBehavior.HoldEnd });
        StartTimer(item);
    }

    private void StartTimer(ToastMessage item)
    {
        if (item.Task || item.Level == ToastLevel.Error || item.Closing
            || !visible.Contains(item) || item.View.IsMouseOver) return;
        item.Timer.Stop();
        item.Timer.Interval = item.Remaining > TimeSpan.Zero ? item.Remaining : TimeSpan.FromMilliseconds(1);
        item.StartedUtc = DateTime.UtcNow;
        item.Timer.Start();
    }

    private void Dismiss(ToastMessage item)
    {
        if (item.Closing || item.Task) return;
        item.Closing = true;
        item.Timer.Stop();
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(170));
        fade.Completed += (_, _) =>
        {
            RemoveNow(item);
            if (pending.Count > 0 && visible.Count < 3) ShowOrQueue(pending.Dequeue());
        };
        item.View.BeginAnimation(OpacityProperty, fade);
    }

    private void RemoveNow(ToastMessage item)
    {
        item.Timer.Stop();
        item.View.BeginAnimation(OpacityProperty, null);
        visible.Remove(item);
        stack.Children.Remove(item.View);
        item.Closing = false;
        item.Remaining = TimeSpan.FromSeconds(5);
    }
}
