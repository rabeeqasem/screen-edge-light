using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using System;

namespace UbuntuEdgeLight;

public partial class ControlWindow : Window
{
    private readonly MainWindow? _mainWindow;
    private bool _initializing = true;

    public ControlWindow(MainWindow mainWindow)
    {
        InitializeComponent();
        _mainWindow = mainWindow;

        // Restore position from the main window's single shared settings instance,
        // but only if that spot is still on a connected screen — a position saved on a
        // since-unplugged monitor would otherwise open the panel invisibly off-screen.
        if (_mainWindow.ControlWindowX is { } x && _mainWindow.ControlWindowY is { } y
            && IsOnAnyScreen(mainWindow, x, y))
        {
            this.Position = new PixelPoint(x, y);
            this.WindowStartupLocation = WindowStartupLocation.Manual;
        }
        else
        {
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        SyncFromMain();
        _initializing = false;
    }

    // Required for XAML previewer
    public ControlWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// True when (x, y) sits far enough inside some connected screen that the panel's
    /// drag handle remains grabbable (60px slack for the panel's header area).
    /// </summary>
    private static bool IsOnAnyScreen(MainWindow main, int x, int y)
    {
        foreach (var screen in main.Screens.All)
        {
            var b = screen.Bounds;
            if (x >= b.X && x <= b.X + b.Width - 60 &&
                y >= b.Y && y <= b.Y + b.Height - 60)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Pushes the current MainWindow state into every control. Guarded so it never
    /// feeds back into the model while updating.
    /// </summary>
    private void SyncFromMain()
    {
        if (_mainWindow == null) return;
        bool prev = _initializing;
        _initializing = true;

        SetSlider("SliderBrightness", _mainWindow.CurrentOpacity);
        SetSlider("SliderColor", _mainWindow.CurrentColorTemp);
        SetSlider("SliderThickness", _mainWindow.CurrentThickness);
        SetSlider("SliderSpread", _mainWindow.CurrentSpread);
        SetSlider("SliderMargin", _mainWindow.CurrentMargin);
        SetSlider("SliderRadius", _mainWindow.CurrentRadius);

        bool flood = _mainWindow.CurrentIllumination == IlluminationMode.StudioFlood;
        var chkFlood = this.FindControl<CheckBox>("ChkFlood");
        if (chkFlood != null) chkFlood.IsChecked = flood;

        var chkDay = this.FindControl<CheckBox>("ChkAutoDayNight");
        if (chkDay != null) chkDay.IsChecked = _mainWindow.CurrentAdaptiveTimeOfDay;

        var chkAmbient = this.FindControl<CheckBox>("ChkAmbient");
        if (chkAmbient != null)
        {
            chkAmbient.IsChecked = _mainWindow.CurrentAdaptiveAmbient;
            // Disable the option entirely when screen sampling isn't available (e.g. Wayland).
            if (!_mainWindow.AmbientAvailable)
            {
                chkAmbient.IsChecked = false;
                chkAmbient.IsEnabled = false;
                chkAmbient.Content = "Match screen ambient (X11 only)";
            }
        }

        UpdateModeVisibility(flood);

        _initializing = prev;
    }

    private void SetSlider(string name, double value)
    {
        var s = this.FindControl<Slider>(name);
        if (s != null) s.Value = value;
    }

    /// <summary>Shows spread for flood mode; thickness/roundness for thin-border mode.</summary>
    private void UpdateModeVisibility(bool flood)
    {
        var spread = this.FindControl<StackPanel>("SpreadPanel");
        var thickness = this.FindControl<StackPanel>("ThicknessPanel");
        var radius = this.FindControl<StackPanel>("RadiusPanel");
        if (spread != null) spread.IsVisible = flood;
        if (thickness != null) thickness.IsVisible = !flood;
        if (radius != null) radius.IsVisible = !flood;
    }

    private void Preset_Click(object? sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;
        if (sender is Button b && b.Tag is string tag && Enum.TryParse<LightMode>(tag, out var mode))
        {
            _mainWindow.ApplyPreset(mode);
            SyncFromMain();
        }
    }

    private void ChkFlood_Changed(object? sender, RoutedEventArgs e)
    {
        if (_initializing || _mainWindow == null) return;
        bool flood = (sender as CheckBox)?.IsChecked == true;
        _mainWindow.SetIlluminationMode(flood ? IlluminationMode.StudioFlood : IlluminationMode.ThinBorder);
        UpdateModeVisibility(flood);
    }

    private void Spotlight_Click(object? sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;
        _mainWindow.ToggleSpotlight();
        SyncFromMain();
    }

    private void ChkAutoDayNight_Changed(object? sender, RoutedEventArgs e)
    {
        if (_initializing || _mainWindow == null) return;
        _mainWindow.SetAdaptiveTimeOfDay((sender as CheckBox)?.IsChecked == true);
        SyncFromMain();
    }

    private void ChkAmbient_Changed(object? sender, RoutedEventArgs e)
    {
        if (_initializing || _mainWindow == null) return;
        _mainWindow.SetAdaptiveAmbient((sender as CheckBox)?.IsChecked == true);
    }

    private void SliderBrightness_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_initializing || _mainWindow == null) return;
        _mainWindow.SetOpacity(e.NewValue);
    }

    private void SliderColor_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_initializing || _mainWindow == null) return;
        _mainWindow.SetColorId(e.NewValue);
    }

    private void SliderThickness_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_initializing || _mainWindow == null) return;
        _mainWindow.SetThickness(e.NewValue);
    }

    private void SliderSpread_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_initializing || _mainWindow == null) return;
        _mainWindow.SetSpread(e.NewValue);
    }

    private void SliderMargin_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_initializing || _mainWindow == null) return;
        _mainWindow.SetMargin(e.NewValue);
    }

    private void SliderRadius_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_initializing || _mainWindow == null) return;
        _mainWindow.SetCornerRadius(e.NewValue);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BeginMoveDrag(e);
    }

    private void SavePosition()
    {
        // Write into the one shared settings instance owned by MainWindow, so the
        // position can't be lost to a second independent AppSettings writer.
        _mainWindow?.SaveControlWindowPosition(this.Position.X, this.Position.Y);
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        SavePosition();
        this.Hide();
        // Restore the overlay's always-on-top (ShowControls lowered it while we were open).
        _mainWindow?.OnControlsClosed();
    }

    private void Exit_Click(object? sender, RoutedEventArgs e)
    {
        SavePosition();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
        else
            Environment.Exit(0);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        SavePosition();
        _mainWindow?.OnControlsClosed();
        base.OnClosing(e);
    }
}
