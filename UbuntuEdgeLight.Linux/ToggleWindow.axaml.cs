using Avalonia.Controls;
using Avalonia.Interactivity;

namespace UbuntuEdgeLight;

/// <summary>
/// Tiny always-on-top affordance to open/close the control panel. The GNOME system
/// tray is unavailable under snap confinement and clicking the app icon won't spawn a
/// second process, so this floating button is the reliable way to get the panel back.
/// </summary>
public partial class ToggleWindow : Window
{
    private readonly MainWindow? _main;

    // Parameterless ctor for the XAML designer only.
    public ToggleWindow()
    {
        InitializeComponent();
    }

    public ToggleWindow(MainWindow main)
    {
        InitializeComponent();
        _main = main;
    }

    private void Toggle_Click(object? sender, RoutedEventArgs e)
    {
        _main?.ToggleControls();
    }
}
