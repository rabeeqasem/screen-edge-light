using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UbuntuEdgeLight;

public partial class MainWindow : Window
{
    private AppSettings _settings;
    private ControlWindow? _controlWindow;
    private ToggleWindow? _toggle;

    // Public properties for binding/accessing from ControlWindow. These are the user's
    // own (saved) values; spotlight and auto day/night override them only at render time.
    public double CurrentOpacity => _opacity;
    public double CurrentColorTemp => _colorTemperature;
    public double CurrentThickness => _thickness;
    public double CurrentMargin => _margin;
    public double CurrentRadius => _cornerRadius;
    public double CurrentSpread => _spread;
    public IlluminationMode CurrentIllumination => _illumination;
    public LightMode CurrentPreset => _preset;
    public bool CurrentAdaptiveTimeOfDay => _adaptiveTimeOfDay;
    public bool CurrentAdaptiveAmbient => _adaptiveAmbient;

    // Control-window position is stored in the one shared settings instance (below).
    public int? ControlWindowX => _settings.ControlWindowX;
    public int? ControlWindowY => _settings.ControlWindowY;

    // Target (logical) values -- what the user asked for. Persisted.
    private double _opacity = 1.0;
    private double _colorTemperature = 0.5;
    private double _thickness = 10.0;
    private double _margin = 0.0;
    private double _cornerRadius = 0.0;
    private double _spread = 180.0;
    private IlluminationMode _illumination = IlluminationMode.ThinBorder;
    private LightMode _preset = LightMode.Custom;
    private bool _adaptiveTimeOfDay;
    private bool _adaptiveAmbient;

    // Time-of-day targets. Never persisted, so turning auto day/night off returns the
    // light to exactly what the user had before.
    private double _todOpacity = 1.0;
    private double _todColorTemperature = 0.5;

    // Currently rendered values -- eased toward the effective targets for smooth transitions.
    private double _curOpacity = 1.0;
    private double _curThickness = 10.0;
    private double _curMargin = 0.0;
    private double _curRadius = 0.0;
    private double _curSpread = 180.0;
    private Color _curColor = Color.FromRgb(255, 255, 255);

    private readonly Color _coolColor = Color.FromRgb(220, 235, 255);
    private readonly Color _warmColor = Color.FromRgb(255, 220, 180);

    // Ambient screen-match tint (X11), updated on a background timer.
    private Color? _ambient;
    private X11AmbientSampler? _sampler;
    private int _samplingBusy; // 0 = idle, 1 = a background Sample() is in flight

    // Spotlight: a temporary full-brightness override on top of the user's values.
    private bool _spotlightOn;

    private DispatcherTimer? _saveTimer;
    private DispatcherTimer? _animTimer;
    private DispatcherTimer? _adaptiveTimer;
    private DispatcherTimer? _ambientTimer;

    // Cached visual references.
    private Border? _border;
    private Grid? _flood;
    private Rectangle? _floodTop;
    private Rectangle? _floodBottom;
    private Rectangle? _floodLeft;
    private Rectangle? _floodRight;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();

        _border = this.FindControl<Border>("EdgeLightBorder");
        _flood = this.FindControl<Grid>("StudioFloodLayer");
        _floodTop = this.FindControl<Rectangle>("FloodTop");
        _floodBottom = this.FindControl<Rectangle>("FloodBottom");
        _floodLeft = this.FindControl<Rectangle>("FloodLeft");
        _floodRight = this.FindControl<Rectangle>("FloodRight");

        // Setup save timer (debounce)
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (s, e) =>
        {
            _saveTimer.Stop();
            _settings.Save();
        };

        // ~60fps easing timer for smooth visual transitions.
        _animTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animTimer.Tick += (s, e) => AnimationTick();

        // Time-of-day adaptation re-evaluates every minute.
        _adaptiveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _adaptiveTimer.Tick += (s, e) => ApplyTimeOfDay();

        // Ambient screen sampling.
        _ambientTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _ambientTimer.Tick += (s, e) => SampleAmbient();

        // Load from settings
        _opacity = _settings.Opacity;
        _colorTemperature = _settings.ColorTemperature;
        _thickness = _settings.BorderThickness;
        _margin = _settings.Margin;
        _cornerRadius = _settings.CornerRadius;
        _spread = _settings.Spread;
        _illumination = _settings.Illumination;
        _preset = _settings.Preset;
        _adaptiveTimeOfDay = _settings.AdaptiveTimeOfDay;
        _adaptiveAmbient = _settings.AdaptiveAmbient;

        if (_adaptiveTimeOfDay)
        {
            UpdateTimeOfDayTargets();
            _adaptiveTimer.Start();
        }
        if (_adaptiveAmbient)
        {
            EnsureSampler();
            if (_sampler?.Available == true)
                _ambientTimer.Start();
        }

        // Start the rendered state already settled on the targets (no startup animation).
        _curOpacity = EffectiveOpacity;
        _curThickness = _thickness;
        _curMargin = _margin;
        _curRadius = _cornerRadius;
        _curSpread = EffectiveSpread;
        _curColor = TargetColor();

        Render();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        PositionWindow();

        // Follow monitor hot-plug, docking and resolution/scale changes; otherwise the
        // overlay keeps its startup geometry and ends up the wrong size or off-screen.
        Screens.Changed += OnScreensChanged;

        // Make the overlay click-through so it never blocks the desktop and can stay
        // permanently visible (no hide-on-interaction needed).
        ApplyClickThrough();

        // Show the floating toggle button and open the control panel on launch. The
        // system tray is unavailable under GNOME + snap confinement, so the always-visible
        // button is the reliable way to reopen the panel after the user closes it.
        Dispatcher.UIThread.Post(() =>
        {
            ShowToggleButton();
            ShowControls();
        }, DispatcherPriority.Background);
    }

    private void OnScreensChanged(object? sender, EventArgs e)
    {
        // Screens reports the change before the new layout has fully settled on some
        // window managers, so re-layout on the next dispatcher pass.
        Dispatcher.UIThread.Post(() =>
        {
            PositionWindow();
            PositionToggleButton();
            ApplyClickThrough();
        }, DispatcherPriority.Background);
    }

    /// <summary>
    /// (Re)apply the empty X11 input shape so the overlay is click-through. Toggling
    /// Topmost can drop the shape, so this is called again whenever Topmost changes.
    /// </summary>
    private void ApplyClickThrough()
    {
        try
        {
            var handle = this.TryGetPlatformHandle();
            if (handle != null)
                X11ClickThrough.Apply(handle.Handle);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Click-through unavailable: {ex.Message}");
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        Screens.Changed -= OnScreensChanged;
        _sampler?.Dispose();
        _toggle?.Close();

        // Flush any debounced save: the 500ms timer never fires once the app is
        // shutting down, which would silently drop the last change / panel position.
        if (_saveTimer is { IsEnabled: true })
        {
            _saveTimer.Stop();
            _settings.Save();
        }
    }

    /// <summary>Show the small always-on-top button used to open/close the control panel.</summary>
    public void ShowToggleButton()
    {
        _toggle ??= new ToggleWindow(this);
        PositionToggleButton();
        _toggle.Show();
        RaiseToggleButton();
    }

    private void PositionToggleButton()
    {
        if (_toggle == null) return;

        // Park it near the bottom-right of the primary working area. Position is in
        // physical pixels while the 44px window size is logical, so scale the inset
        // (44 + 20 margin) or the button lands partly off-screen on HiDPI displays.
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen != null)
        {
            var area = screen.WorkingArea;
            int inset = (int)Math.Round(64 * screen.Scaling);
            _toggle.Position = new PixelPoint(area.X + area.Width - inset, area.Y + area.Height - inset);
        }
    }

    /// <summary>
    /// Put the floating button back above the overlay. Both windows are always-on-top,
    /// so whichever was raised last wins; re-asserting the overlay's Topmost would
    /// otherwise bury the button under the (brightest, corner) part of the light.
    /// </summary>
    private void RaiseToggleButton()
    {
        if (_toggle == null || !_toggle.IsVisible) return;
        _toggle.Topmost = false;
        _toggle.Topmost = true;
    }

    /// <summary>Show or hide the light itself (used by `screen-edge-light --toggle`).</summary>
    public void ToggleLight()
    {
        if (IsVisible)
        {
            Hide();
        }
        else
        {
            Show();
            PositionWindow();
            SetOverlayTopmost(_controlWindow is not { IsVisible: true });
        }
    }

    /// <summary>Open the panel if it's hidden, hide it if it's showing.</summary>
    public void ToggleControls()
    {
        if (_controlWindow != null && _controlWindow.IsVisible)
        {
            _controlWindow.Hide();
            OnControlsClosed();
        }
        else
        {
            ShowControls();
        }
    }

    public void ShowControls()
    {
        if (_controlWindow == null)
        {
            _controlWindow = new ControlWindow(this);
            _controlWindow.Closed += (s, e) =>
            {
                _controlWindow = null;
                SetOverlayTopmost(true);
            };
        }

        // Lower the main window Z-order so the controls float above the overlay.
        SetOverlayTopmost(false);

        _controlWindow.Show();
        _controlWindow.Activate();
        _controlWindow.Topmost = true;

        // Keep the floating button above the panel so it can always close it again.
        _toggle?.Show();
        RaiseToggleButton();
    }

    /// <summary>Called by the control window when it is hidden, to restore always-on-top.</summary>
    public void OnControlsClosed()
    {
        SetOverlayTopmost(true);
    }

    /// <summary>
    /// Change the overlay's always-on-top state and re-assert click-through, because
    /// raising/lowering the X11 window can drop the input shape and start eating clicks.
    /// </summary>
    private void SetOverlayTopmost(bool topmost)
    {
        this.Topmost = topmost;
        ApplyClickThrough();
        RaiseToggleButton();
    }

    private void PositionWindow()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen != null)
        {
            var area = screen.WorkingArea;
            this.Position = new PixelPoint(area.X, area.Y);

            var scaling = screen.Scaling;
            this.Width = area.Width / scaling;
            this.Height = area.Height / scaling;

            this.WindowState = WindowState.Normal;
        }
    }

    private void QueueSave()
    {
        _saveTimer?.Stop();
        _saveTimer?.Start();
    }

    /// <summary>
    /// Persist the control-window position into the one shared settings instance, so it
    /// can't be clobbered by a second AppSettings object writing the same file.
    /// </summary>
    public void SaveControlWindowPosition(int x, int y)
    {
        _settings.ControlWindowX = x;
        _settings.ControlWindowY = y;
        QueueSave();
    }

    // --- Manual adjustments (slider driven) ---

    public void SetOpacity(double value)
    {
        _opacity = Math.Clamp(value, 0.2, 1.0);
        _settings.Opacity = _opacity;
        ExitSpotlight();
        MarkCustom();
        QueueSave();
        StartAnim();
    }

    public void SetThickness(double value)
    {
        _thickness = Math.Clamp(value, 1.0, 50.0);
        _settings.BorderThickness = _thickness;
        MarkCustom();
        QueueSave();
        StartAnim();
    }

    public void SetMargin(double value)
    {
        _margin = Math.Max(0.0, value);
        _settings.Margin = _margin;
        QueueSave();
        StartAnim();
    }

    public void SetCornerRadius(double value)
    {
        _cornerRadius = Math.Max(0.0, value);
        _settings.CornerRadius = _cornerRadius;
        QueueSave();
        StartAnim();
    }

    public void SetColorId(double value)
    {
        _colorTemperature = Math.Clamp(value, 0.0, 1.0);
        _settings.ColorTemperature = _colorTemperature;
        ExitSpotlight();
        MarkCustom();
        QueueSave();
        StartAnim();
    }

    // --- Studio Light: spread, illumination mode, presets ---

    public void SetSpread(double value)
    {
        _spread = Math.Clamp(value, 20.0, 600.0);
        _settings.Spread = _spread;
        ExitSpotlight();
        MarkCustom();
        QueueSave();
        StartAnim();
    }

    public void SetIlluminationMode(IlluminationMode mode)
    {
        _illumination = mode;
        _settings.Illumination = mode;
        ExitSpotlight();
        MarkCustom();
        QueueSave();
        Render();
        StartAnim();
    }

    private void MarkCustom()
    {
        if (_preset != LightMode.Custom)
        {
            _preset = LightMode.Custom;
            _settings.Preset = LightMode.Custom;
        }
    }

    public void ApplyPreset(LightMode mode)
    {
        switch (mode)
        {
            case LightMode.SoftKey:
                _opacity = 0.85; _colorTemperature = 0.45; _thickness = 14; _spread = 180;
                _illumination = IlluminationMode.StudioFlood; break;
            case LightMode.WarmStudio:
                _opacity = 0.90; _colorTemperature = 0.82; _thickness = 16; _spread = 220;
                _illumination = IlluminationMode.StudioFlood; break;
            case LightMode.Daylight:
                _opacity = 1.00; _colorTemperature = 0.40; _thickness = 12; _spread = 200;
                _illumination = IlluminationMode.StudioFlood; break;
            case LightMode.Cool:
                _opacity = 0.95; _colorTemperature = 0.10; _thickness = 12; _spread = 160;
                _illumination = IlluminationMode.StudioFlood; break;
            case LightMode.Candle:
                _opacity = 0.60; _colorTemperature = 1.00; _thickness = 10; _spread = 140;
                _illumination = IlluminationMode.StudioFlood; break;
            case LightMode.Custom:
            default:
                return;
        }

        _preset = mode;

        // An explicit preset choice wins over the temporary overrides: otherwise spotlight
        // would hide it, and auto day/night would replace its color within a minute.
        _spotlightOn = false;
        if (_adaptiveTimeOfDay)
            SetAdaptiveTimeOfDay(false);

        PersistAll();
        QueueSave();
        Render();
        StartAnim();
    }

    public void ToggleSpotlight()
    {
        _spotlightOn = !_spotlightOn;
        Render();
        StartAnim();
    }

    private void ExitSpotlight()
    {
        // Adjusting the light by hand means the user wants to see their change, which
        // spotlight's fixed full-brightness look would otherwise mask.
        if (_spotlightOn)
        {
            _spotlightOn = false;
            Render();
        }
    }

    public bool SpotlightOn => _spotlightOn;

    // --- Effective values: the user's values with spotlight / auto day-night applied ---

    private double EffectiveOpacity =>
        _spotlightOn ? 1.0 : _adaptiveTimeOfDay ? _todOpacity : _opacity;

    private double EffectiveColorTemperature =>
        _spotlightOn ? 0.5 : _adaptiveTimeOfDay ? _todColorTemperature : _colorTemperature;

    private double EffectiveSpread => _spotlightOn ? Math.Max(_spread, 280) : _spread;

    private IlluminationMode EffectiveIllumination =>
        _spotlightOn ? IlluminationMode.StudioFlood : _illumination;

    // --- Adaptive engine ---

    public void SetAdaptiveTimeOfDay(bool enabled)
    {
        _adaptiveTimeOfDay = enabled;
        _settings.AdaptiveTimeOfDay = enabled;
        QueueSave();

        if (enabled)
        {
            ApplyTimeOfDay();
            _adaptiveTimer?.Start();
        }
        else
        {
            _adaptiveTimer?.Stop();
            StartAnim(); // ease back to the user's own brightness/color
        }
    }

    public void SetAdaptiveAmbient(bool enabled)
    {
        _adaptiveAmbient = enabled;
        _settings.AdaptiveAmbient = enabled;
        QueueSave();

        if (enabled)
        {
            EnsureSampler();
            if (_sampler?.Available == true)
            {
                _ambientTimer?.Start();
                SampleAmbient();
            }
        }
        else
        {
            _ambientTimer?.Stop();
            _ambient = null;
            StartAnim();
        }
    }

    /// <summary>True only when ambient sampling is actually usable (X11 session).</summary>
    public bool AmbientAvailable
    {
        get
        {
            EnsureSampler();
            return _sampler?.Available == true;
        }
    }

    private void EnsureSampler()
    {
        _sampler ??= new X11AmbientSampler();
    }

    private void SampleAmbient()
    {
        if (!_adaptiveAmbient || _sampler?.Available != true)
            return;

        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen == null)
            return;

        // Sample only the part of the screen the light doesn't cover. Sampling the edges
        // would mostly measure our own glow, and the tint would drift toward itself.
        double lightReach = _margin + (EffectiveIllumination == IlluminationMode.StudioFlood
            ? EffectiveSpread
            : _thickness * 6);
        int inset = (int)Math.Round((lightReach + 24) * screen.Scaling);
        var b = screen.Bounds;
        int rx = b.X + inset, ry = b.Y + inset;
        int rw = b.Width - 2 * inset, rh = b.Height - 2 * inset;
        if (rw < 64 || rh < 64)
            return;

        // Skip if a previous sample is still running, so we never issue two concurrent
        // XGetImage calls against the sampler's single Xlib connection.
        if (Interlocked.Exchange(ref _samplingBusy, 1) == 1)
            return;

        var sampler = _sampler;
        Task.Run(() =>
        {
            try
            {
                var c = sampler.Sample(rx, ry, rw, rh);
                if (c is { } v)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        _ambient = Color.FromRgb(v.R, v.G, v.B);
                        StartAnim();
                    });
                }
            }
            finally
            {
                Interlocked.Exchange(ref _samplingBusy, 0);
            }
        });
    }

    private void ApplyTimeOfDay()
    {
        if (!_adaptiveTimeOfDay)
            return;

        UpdateTimeOfDayTargets();
        StartAnim();
    }

    private void UpdateTimeOfDayTargets()
    {
        // dayFactor: +1 around 13:00 (coolest/brightest), -1 around 01:00 (warmest/dimmest).
        double hours = DateTime.Now.TimeOfDay.TotalHours;
        double dayFactor = Math.Cos((hours - 13.0) / 24.0 * 2.0 * Math.PI);

        // Map [-1, +1] -> targets.
        _todColorTemperature = Map(dayFactor, 1.0, -1.0, 0.35, 0.90);
        _todOpacity = Math.Clamp(Map(dayFactor, 1.0, -1.0, 1.00, 0.70), 0.2, 1.0);
    }

    private static double Map(double v, double inMin, double inMax, double outMin, double outMax)
    {
        if (Math.Abs(inMax - inMin) < 1e-9) return outMin;
        double t = (v - inMin) / (inMax - inMin);
        t = Math.Clamp(t, 0.0, 1.0);
        return outMin + (outMax - outMin) * t;
    }

    private void PersistAll()
    {
        _settings.Opacity = _opacity;
        _settings.ColorTemperature = _colorTemperature;
        _settings.BorderThickness = _thickness;
        _settings.Margin = _margin;
        _settings.CornerRadius = _cornerRadius;
        _settings.Spread = _spread;
        _settings.Illumination = _illumination;
        _settings.Preset = _preset;
    }

    // --- Rendering & easing ---

    private Color TargetColor()
    {
        var baseColor = LerpColor(_coolColor, _warmColor, EffectiveColorTemperature);
        if (_adaptiveAmbient && _ambient is { } amb)
        {
            baseColor = LerpColor(baseColor, amb, 0.35);
        }
        return baseColor;
    }

    private void StartAnim()
    {
        if (_animTimer is { IsEnabled: false })
            _animTimer.Start();
    }

    private void AnimationTick()
    {
        const double k = 0.22;
        var target = TargetColor();
        double opacity = EffectiveOpacity;
        double spread = EffectiveSpread;

        _curOpacity = Lerp(_curOpacity, opacity, k);
        _curThickness = Lerp(_curThickness, _thickness, k);
        _curMargin = Lerp(_curMargin, _margin, k);
        _curRadius = Lerp(_curRadius, _cornerRadius, k);
        _curSpread = Lerp(_curSpread, spread, k);
        _curColor = LerpColor(_curColor, target, k);

        Render();

        bool settled =
            Close(_curOpacity, opacity, 0.002) &&
            Close(_curThickness, _thickness, 0.05) &&
            Close(_curMargin, _margin, 0.05) &&
            Close(_curRadius, _cornerRadius, 0.05) &&
            Close(_curSpread, spread, 0.2) &&
            ColorClose(_curColor, target, 2);

        if (settled)
        {
            _curOpacity = opacity;
            _curThickness = _thickness;
            _curMargin = _margin;
            _curRadius = _cornerRadius;
            _curSpread = spread;
            _curColor = target;
            Render();
            _animTimer?.Stop();
        }
    }

    private void Render()
    {
        bool flood = EffectiveIllumination == IlluminationMode.StudioFlood;

        if (_flood != null) _flood.IsVisible = flood;
        if (_border != null) _border.IsVisible = !flood;

        if (flood)
        {
            if (_flood != null)
            {
                _flood.Opacity = _curOpacity;
                _flood.Margin = new Thickness(_curMargin);
            }
            ApplyFloodColor(_floodTop, _curColor, _curSpread, isHorizontal: false);
            ApplyFloodColor(_floodBottom, _curColor, _curSpread, isHorizontal: false);
            ApplyFloodColor(_floodLeft, _curColor, _curSpread, isHorizontal: true);
            ApplyFloodColor(_floodRight, _curColor, _curSpread, isHorizontal: true);
        }
        else if (_border != null)
        {
            _border.Opacity = _curOpacity;
            _border.BorderThickness = new Thickness(_curThickness);
            _border.CornerRadius = new CornerRadius(_curRadius);
            _border.Margin = new Thickness(_curMargin);

            int blur = (int)Math.Round(_curThickness * 5);
            int spreadPx = (int)Math.Round(_curThickness);
            _border.BoxShadow = BoxShadows.Parse($"0 0 {blur} {spreadPx} {ToHex(_curColor)}");

            if (_border.BorderBrush is LinearGradientBrush gradient && gradient.GradientStops.Count >= 5)
            {
                var dim = Color.FromRgb(
                    (byte)(_curColor.R * 0.9),
                    (byte)(_curColor.G * 0.9),
                    (byte)(_curColor.B * 0.9));

                gradient.GradientStops[0].Color = _curColor;
                gradient.GradientStops[1].Color = dim;
                gradient.GradientStops[2].Color = _curColor;
                gradient.GradientStops[3].Color = dim;
                gradient.GradientStops[4].Color = _curColor;
            }
        }
    }

    private static void ApplyFloodColor(Rectangle? rect, Color color, double spread, bool isHorizontal)
    {
        if (rect == null) return;

        if (isHorizontal) rect.Width = spread;
        else rect.Height = spread;

        if (rect.Fill is LinearGradientBrush g && g.GradientStops.Count >= 2)
        {
            g.GradientStops[0].Color = Color.FromArgb(255, color.R, color.G, color.B);
            g.GradientStops[1].Color = Color.FromArgb(0, color.R, color.G, color.B);
        }
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static bool Close(double a, double b, double eps) => Math.Abs(a - b) <= eps;

    private static bool ColorClose(Color a, Color b, int eps) =>
        Math.Abs(a.R - b.R) <= eps && Math.Abs(a.G - b.G) <= eps && Math.Abs(a.B - b.B) <= eps;

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private Color LerpColor(Color a, Color b, double t)
    {
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t)
        );
    }
}
