using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Muses.Core.Chrome;

namespace Muses.App.Theme;

/// <summary>
/// Practical Win11 glass pass for Muses chrome.
/// Window keeps TransparencyLevelHint=Mica; this swaps DynamicResource fills and
/// toggles ExperimentalAcrylicBorder layers according to <see cref="GlassDecision"/>.
/// </summary>
public static class GlassChrome
{
    public const string SidebarBrushKey = "MusesGlassSidebarBrush";
    public const string PanelBrushKey = "MusesGlassPanelBrush";
    public const string CapsuleBrushKey = "MusesGlassCapsuleBrush";
    public const string DrawerBrushKey = "MusesGlassDrawerBrush";

    public static GlassMode CurrentMode { get; private set; } = GlassMode.Material;

    private static bool _resourcesReady;
    private static TopLevel? _attached;
    private static bool _listeningSettings;

    public static void EnsureResources(Application app)
    {
        if (_resourcesReady) return;
        var res = app.Resources;
        res[SidebarBrushKey] = ThemeBrushes.SurfaceChrome;
        res[PanelBrushKey] = ThemeBrushes.SurfaceChrome;
        res[CapsuleBrushKey] = ThemeBrushes.Capsule;
        res[DrawerBrushKey] = ThemeBrushes.Drawer;
        _resourcesReady = true;
    }

    /// <summary>
    /// Attach to the main window: re-evaluate when transparency or contrast changes.
    /// </summary>
    public static void Attach(TopLevel topLevel)
    {
        var app = Application.Current;
        if (app is null) return;

        EnsureResources(app);

        if (!ReferenceEquals(_attached, topLevel))
        {
            if (_attached is not null)
                _attached.PropertyChanged -= OnTopLevelPropertyChanged;
            _attached = topLevel;
            topLevel.PropertyChanged += OnTopLevelPropertyChanged;
        }

        if (!_listeningSettings)
        {
            try
            {
                if (app.PlatformSettings is { } ps)
                {
                    ps.ColorValuesChanged += (_, _) =>
                    {
                        if (_attached is not null)
                            Apply(_attached);
                    };
                    _listeningSettings = true;
                }
            }
            catch
            {
                // Platforms without color-value notifications are fine.
            }
        }

        Apply(topLevel);
    }

    private static void OnTopLevelPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TopLevel.ActualTransparencyLevelProperty && sender is TopLevel top)
            Apply(top);
    }

    public static void Apply(TopLevel topLevel)
    {
        var app = Application.Current;
        if (app is null) return;
        EnsureResources(app);

        var increaseContrast = false;
        try
        {
            var values = app.PlatformSettings?.GetColorValues();
            if (values is not null)
                increaseContrast = values.ContrastPreference == ColorContrastPreference.High;
        }
        catch
        {
            // Treat as contrast off.
        }

        var level = topLevel.ActualTransparencyLevel;
        var supportsGlass =
            level == WindowTransparencyLevel.Mica
            || level == WindowTransparencyLevel.AcrylicBlur
            || level == WindowTransparencyLevel.Blur;

        // Avalonia does not expose Win11 "Transparency effects" / Reduce Transparency
        // directly. High contrast → Opaque. Otherwise Glass when Mica/Acrylic is active,
        // Material (translucent token fills) when the host only offers Transparent/None
        // (macOS/Linux/Skia, or Win11 with effects disabled).
        const bool reduceTransparency = false;

        CurrentMode = GlassDecision.Mode(reduceTransparency, increaseContrast, supportsGlass);

        var res = app.Resources;
        switch (CurrentMode)
        {
            case GlassMode.Opaque:
                res[SidebarBrushKey] = CloneBrush(ThemeBrushes.SurfaceBrush);
                res[PanelBrushKey] = CloneBrush(ThemeBrushes.SurfaceBrush);
                res[CapsuleBrushKey] = CloneBrush(ThemeBrushes.SurfaceBrush);
                res[DrawerBrushKey] = CloneBrush(ThemeBrushes.SurfaceBrush);
                break;

            case GlassMode.Glass:
                // Leaner fills so window Mica reads through chrome.
                res[SidebarBrushKey] = CloneBrush(ThemeBrushes.Capsule);
                res[PanelBrushKey] = CloneBrush(ThemeBrushes.Capsule);
                res[CapsuleBrushKey] = CloneBrush(ThemeBrushes.Capsule);
                res[DrawerBrushKey] = CloneBrush(ThemeBrushes.SurfaceChrome);
                break;

            default:
                res[SidebarBrushKey] = CloneBrush(ThemeBrushes.SurfaceChrome);
                res[PanelBrushKey] = CloneBrush(ThemeBrushes.SurfaceChrome);
                res[CapsuleBrushKey] = CloneBrush(ThemeBrushes.Capsule);
                res[DrawerBrushKey] = CloneBrush(ThemeBrushes.Drawer);
                break;
        }
    }

    public static ExperimentalAcrylicMaterial? CreateAcrylicMaterial(MusesGlassRole role)
    {
        if (CurrentMode != GlassMode.Glass)
            return null;

        var (tintOpacity, materialOpacity) = role switch
        {
            MusesGlassRole.Player => (0.55, 0.70),
            MusesGlassRole.FloatingPanel => (0.60, 0.75),
            MusesGlassRole.CompactControl => (0.50, 0.65),
            _ => (0.65, 0.80)
        };

        return new ExperimentalAcrylicMaterial
        {
            BackgroundSource = AcrylicBackgroundSource.Digger,
            TintColor = ThemeBrushes.SurfaceColor,
            TintOpacity = tintOpacity,
            MaterialOpacity = materialOpacity
        };
    }

    /// <summary>
    /// Show acrylic only in Glass mode; hide for Material/Opaque.
    /// </summary>
    public static void SyncAcrylic(ExperimentalAcrylicBorder? acrylic, MusesGlassRole role)
    {
        if (acrylic is null) return;
        var material = CreateAcrylicMaterial(role);
        if (material is null)
        {
            acrylic.IsVisible = false;
            return;
        }

        acrylic.Material = material;
        acrylic.IsVisible = true;
    }

    private static IBrush CloneBrush(IBrush source)
    {
        if (source is ISolidColorBrush solid)
            return new SolidColorBrush(solid.Color);
        return source;
    }
}
