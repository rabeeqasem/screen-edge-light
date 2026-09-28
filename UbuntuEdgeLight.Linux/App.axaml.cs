using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Controls;
using System;

namespace UbuntuEdgeLight;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // We don't want to close the app when the main window closes
            desktop.MainWindow = new MainWindow();
            
            // Start IPC Server
            Program.StartIpcServer(desktop.MainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }

    public static void ToggleLight(Window? window)
    {
        if (window != null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                if (window.IsVisible)
                    window.Hide();
                else
                    window.Show();
            });
        }
    }

    private void ToggleLight_Click(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
             ToggleLight(desktop.MainWindow);
        }
    }

    private void ShowControls_Click(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && desktop.MainWindow is MainWindow main)
        {
            main.ShowControls();
        }
    }

    private void Exit_Click(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
