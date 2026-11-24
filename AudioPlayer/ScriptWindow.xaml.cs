using System.ComponentModel;
using System.Windows;

namespace AudioPlayer;

public partial class ScriptWindow : Window
{
    private bool ownerClosing;

    public ScriptWindow() => InitializeComponent();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (ownerClosing) return;
        e.Cancel = true;
        Hide();
    }

    internal void CloseForOwner()
    {
        ownerClosing = true;
        Close();
    }
}
