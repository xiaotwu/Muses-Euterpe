using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Muses.Core.Chrome;

namespace Muses.App.Theme;

internal static class GlassChrome
{
    private const string Regular = "MusesGlassCapsuleBrush";
    private const string Clear = "MusesGlassClearBrush";
    private const string Rim = "MusesGlassRimBrush";
    private const string Panel = "MusesGlassPanelBrush";
    private const string Sidebar = "MusesGlassSidebarBrush";
    private const string Drawer = "MusesGlassDrawerBrush";
    private const string DockShadow = "MusesGlassDockShadow";
    private const string PlaybackShadow = "MusesGlassPlaybackShadow";
    private const string AtmosphereOpacity = "MusesGlassAtmosphereOpacity";
    private static Action? _refresh;

    internal static void Refresh() => _refresh?.Invoke();

    internal static void Apply(Application app)
    {
        if (app.Resources is not ResourceDictionary resources || app.PlatformSettings is not { } settings)
            return;

        var normalEffects = Capture(resources, DockShadow, PlaybackShadow, AtmosphereOpacity);
        var normalPalettes = resources.ThemeDictionaries.Values.OfType<ResourceDictionary>()
            .ToDictionary(palette => palette, palette => Capture(palette, Regular, Clear, Rim, Panel, Sidebar, Drawer));
        var isOpaque = false;

        void Update(PlatformColorValues colors)
        {
            var reduceTransparency = OperatingSystem.IsWindows()
                && SystemParametersInfo(0x1040 /* SPI_GETDISABLEOVERLAPPEDCONTENT */, 0, out var disabled, 0)
                && disabled;
            var opaque = GlassDecision.Mode(reduceTransparency,
                colors.ContrastPreference == ColorContrastPreference.High, supportsGlass: false) == GlassMode.Opaque;
            if (isOpaque == opaque)
                return;

            isOpaque = opaque;
            foreach (var (palette, brushes) in normalPalettes)
            {
                var surface = palette["MusesSurfaceBrush"];
                palette.SetItems(opaque
                    ? [new(Regular, surface), new(Clear, surface), new(Panel, surface),
                       new(Sidebar, surface), new(Drawer, surface), new(Rim, palette["MusesTextPrimaryBrush"])]
                    : brushes);
            }

            resources.SetItems(opaque
                ? [new(DockShadow, default(BoxShadows)), new(PlaybackShadow, default(BoxShadows)), new(AtmosphereOpacity, 0.0)]
                : normalEffects);
        }

        _refresh = () => Update(settings.GetColorValues());
        settings.ColorValuesChanged += (_, _) => Dispatcher.UIThread.Post(Refresh);
        Refresh();
    }

    private static KeyValuePair<object, object?>[] Capture(ResourceDictionary resources, params string[] keys) =>
        keys.Select(key => new KeyValuePair<object, object?>(key, resources[key])).ToArray();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter,
        [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
}
