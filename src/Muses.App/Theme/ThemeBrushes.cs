using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Muses.Core.Chrome;

namespace Muses.App.Theme;

/// <summary>
/// Resolves Muses chrome brushes/colors from Application resources when available,
/// falling back to BrandColors / known hex tokens for design-time and early init.
/// </summary>
public static class ThemeBrushes
{
    public static bool IsDark { get; set; } = true;

    public static Color Color(Rgba rgba) =>
        Avalonia.Media.Color.FromArgb(rgba.ByteA, rgba.ByteR, rgba.ByteG, rgba.ByteB);

    public static SolidColorBrush Solid(ThemePair pair) => new(Color(pair.For(IsDark)));

    // --- Legacy BrandColors-backed aliases ---
    public static IBrush Background => Page;
    public static IBrush Surface => SurfaceBrush;
    public static IBrush Magenta => Accent;
    public static IBrush TextPrimary => TextPrimaryBrush;
    public static IBrush TextSecondary => TextSecondaryBrush;
    public static IBrush Hairline => HairlineBrush;
    public static IBrush Scrim => ScrimBrush;
    public static IBrush YouTubeRed => YouTubeRedBrush;

    // --- Resource-backed brushes (match App.axaml brush keys) ---
    public static IBrush Accent => BrushOr("MusesAccentBrush", () => Solid(BrandColors.Magenta));
    public static IBrush Page => BrushOr("MusesPageBrush", () => Solid(BrandColors.Background));
    public static IBrush SurfaceBrush => BrushOr("MusesSurfaceBrush", () => Solid(BrandColors.Surface));
    public static IBrush TextPrimaryBrush => BrushOr("MusesTextPrimaryBrush", () => Solid(BrandColors.TextPrimary));
    public static IBrush TextSecondaryBrush => BrushOr("MusesTextSecondaryBrush", () => Solid(BrandColors.TextSecondary));
    public static IBrush TextMuted => BrushOr("MusesTextMutedBrush", () => ParseBrush("#B3B3B8"));
    public static IBrush HairlineBrush => BrushOr("MusesHairlineBrush", () => Solid(BrandColors.Hairline));
    public static IBrush ScrimBrush => BrushOr("MusesScrimBrush", () => Solid(BrandColors.Scrim));
    public static IBrush YouTubeRedBrush => BrushOr("MusesYouTubeRedBrush", () => new SolidColorBrush(Color(BrandColors.YouTubeRed)));
    public static IBrush SurfaceChrome => BrushOr("MusesSurfaceChromeBrush", () => ParseBrush("#E626262B"));
    public static IBrush Capsule => BrushOr("MusesCapsuleBrush", () => ParseBrush("#CC26262B"));
    public static IBrush Drawer => BrushOr("MusesDrawerBrush", () => ParseBrush("#F226262B"));
    public static IBrush PageOpaqueDark => BrushOr("MusesPageOpaqueDarkBrush", () => ParseBrush("#D91F1F1F"));
    public static IBrush SurfaceOverlay1F => BrushOr("MusesSurfaceOverlay1FBrush", () => ParseBrush("#1F26262B"));

    public static IBrush WhiteOverlay0A => BrushOr("MusesWhiteOverlay0ABrush", () => ParseBrush("#0AFFFFFF"));
    public static IBrush WhiteOverlay14 => BrushOr("MusesWhiteOverlay14Brush", () => ParseBrush("#14FFFFFF"));
    public static IBrush WhiteOverlay1A => BrushOr("MusesWhiteOverlay1ABrush", () => ParseBrush("#1AFFFFFF"));
    public static IBrush WhiteOverlay1E => BrushOr("MusesWhiteOverlay1EBrush", () => ParseBrush("#1EFFFFFF"));
    public static IBrush WhiteOverlay24 => BrushOr("MusesWhiteOverlay24Brush", () => ParseBrush("#24FFFFFF"));
    public static IBrush WhiteOverlay26 => BrushOr("MusesWhiteOverlay26Brush", () => ParseBrush("#26FFFFFF"));
    public static IBrush WhiteOverlay2A => BrushOr("MusesWhiteOverlay2ABrush", () => ParseBrush("#2AFFFFFF"));
    public static IBrush WhiteOverlay2E => BrushOr("MusesWhiteOverlay2EBrush", () => ParseBrush("#2EFFFFFF"));
    public static IBrush WhiteOverlay33 => BrushOr("MusesWhiteOverlay33Brush", () => ParseBrush("#33FFFFFF"));
    public static IBrush WhiteOverlay38 => BrushOr("MusesWhiteOverlay38Brush", () => ParseBrush("#38FFFFFF"));
    public static IBrush WhiteOverlay40 => BrushOr("MusesWhiteOverlay40Brush", () => ParseBrush("#40FFFFFF"));
    public static IBrush WhiteOverlay47 => BrushOr("MusesWhiteOverlay47Brush", () => ParseBrush("#47FFFFFF"));
    public static IBrush WhiteOverlay59 => BrushOr("MusesWhiteOverlay59Brush", () => ParseBrush("#59FFFFFF"));
    public static IBrush WhiteOverlay61 => BrushOr("MusesWhiteOverlay61Brush", () => ParseBrush("#61FFFFFF"));
    public static IBrush WhiteOverlay66 => BrushOr("MusesWhiteOverlay66Brush", () => ParseBrush("#66FFFFFF"));
    public static IBrush WhiteOverlay73 => BrushOr("MusesWhiteOverlay73Brush", () => ParseBrush("#73FFFFFF"));
    public static IBrush WhiteOverlay80 => BrushOr("MusesWhiteOverlay80Brush", () => ParseBrush("#80FFFFFF"));
    public static IBrush WhiteOverlay99 => BrushOr("MusesWhiteOverlay99Brush", () => ParseBrush("#99FFFFFF"));
    public static IBrush WhiteOverlayB3 => BrushOr("MusesWhiteOverlayB3Brush", () => ParseBrush("#B3FFFFFF"));
    public static IBrush WhiteOverlayCC => BrushOr("MusesWhiteOverlayCCBrush", () => ParseBrush("#CCFFFFFF"));
    public static IBrush WhiteOverlayD1 => BrushOr("MusesWhiteOverlayD1Brush", () => ParseBrush("#D1FFFFFF"));
    public static IBrush WhiteOverlayE6 => BrushOr("MusesWhiteOverlayE6Brush", () => ParseBrush("#E6FFFFFF"));

    public static IBrush AccentOverlay14 => BrushOr("MusesAccentOverlay14Brush", () => ParseBrush("#14FA586A"));
    public static IBrush AccentOverlay1A => BrushOr("MusesAccentOverlay1ABrush", () => ParseBrush("#1AFA586A"));
    public static IBrush AccentOverlay26 => BrushOr("MusesAccentOverlay26Brush", () => ParseBrush("#26FA586A"));
    public static IBrush AccentOverlay33 => BrushOr("MusesAccentOverlay33Brush", () => ParseBrush("#33FA586A"));
    public static IBrush AccentOverlay59 => BrushOr("MusesAccentOverlay59Brush", () => ParseBrush("#59FA586A"));
    public static IBrush AccentOverlay85 => BrushOr("MusesAccentOverlay85Brush", () => ParseBrush("#85FA586A"));
    public static IBrush AccentOverlay99 => BrushOr("MusesAccentOverlay99Brush", () => ParseBrush("#99FA586A"));
    public static IBrush AccentOverlayD9 => BrushOr("MusesAccentOverlayD9Brush", () => ParseBrush("#D9FA586A"));
    public static IBrush AccentOverlayE6 => BrushOr("MusesAccentOverlayE6Brush", () => ParseBrush("#E6FA586A"));

    public static IBrush BlackOverlay33 => BrushOr("MusesBlackOverlay33Brush", () => ParseBrush("#33000000"));
    public static IBrush BlackOverlay40 => BrushOr("MusesBlackOverlay40Brush", () => ParseBrush("#40000000"));
    public static IBrush BlackOverlay4D => BrushOr("MusesBlackOverlay4DBrush", () => ParseBrush("#4D000000"));
    public static IBrush BlackOverlay55 => BrushOr("MusesBlackOverlay55Brush", () => ParseBrush("#55000000"));
    public static IBrush BlackOverlay59 => BrushOr("MusesBlackOverlay59Brush", () => ParseBrush("#59000000"));
    public static IBrush BlackOverlay60 => BrushOr("MusesBlackOverlay60Brush", () => ParseBrush("#60000000"));
    public static IBrush BlackOverlay66 => BrushOr("MusesBlackOverlay66Brush", () => ParseBrush("#66000000"));
    public static IBrush BlackOverlay73 => BrushOr("MusesBlackOverlay73Brush", () => ParseBrush("#73000000"));
    public static IBrush BlackOverlay80 => BrushOr("MusesBlackOverlay80Brush", () => ParseBrush("#80000000"));
    public static IBrush BlackOverlay99 => BrushOr("MusesBlackOverlay99Brush", () => ParseBrush("#99000000"));
    public static IBrush BlackOverlayB3 => BrushOr("MusesBlackOverlayB3Brush", () => ParseBrush("#B3000000"));

    public static IBrush TextSecondaryMuted => BrushOr("MusesTextSecondaryMutedBrush", () => ParseBrush("#80A6A6AD"));
    public static IBrush TextPrimaryMuted => BrushOr("MusesTextPrimaryMutedBrush", () => ParseBrush("#CCF0F0F0"));

    // --- Color tokens (for APIs that require Color, e.g. title bar) ---
    public static Avalonia.Media.Color AccentColor => ColorOr("MusesAccent", () => Color(BrandColors.Magenta.For(IsDark)));
    public static Avalonia.Media.Color PageColor => ColorOr("MusesPageDark", () => Color(BrandColors.Background.For(IsDark)));
    public static Avalonia.Media.Color SurfaceColor => ColorOr("MusesSurfaceDark", () => Color(BrandColors.Surface.For(IsDark)));
    public static Avalonia.Media.Color TextPrimaryColor => ColorOr("MusesTextPrimary", () => Color(BrandColors.TextPrimary.For(IsDark)));
    public static Avalonia.Media.Color TextSecondaryColor => ColorOr("MusesTextSecondary", () => Color(BrandColors.TextSecondary.For(IsDark)));
    public static Avalonia.Media.Color TextMutedColor => ColorOr("MusesTextMuted", () => Avalonia.Media.Color.Parse("#B3B3B8"));
    public static Avalonia.Media.Color YouTubeRedColor => ColorOr("MusesYouTubeRed", () => Color(BrandColors.YouTubeRed));
    public static Avalonia.Media.Color ScrimColor => ColorOr("MusesScrim", () => Color(BrandColors.Scrim.For(IsDark)));
    public static Avalonia.Media.Color HairlineColor => ColorOr("MusesHairline", () => Color(BrandColors.Hairline.For(IsDark)));

    public static IBrush BrushOr(string key, Func<IBrush> fallback)
    {
        if (TryGetResource(key, out var value))
        {
            if (value is IBrush brush)
                return brush;
            if (value is Avalonia.Media.Color color)
                return new SolidColorBrush(color);
        }
        return fallback();
    }

    public static Avalonia.Media.Color ColorOr(string key, Func<Avalonia.Media.Color> fallback)
    {
        if (TryGetResource(key, out var value))
        {
            if (value is Avalonia.Media.Color color)
                return color;
            if (value is SolidColorBrush scb)
                return scb.Color;
            if (value is ISolidColorBrush iscb)
                return iscb.Color;
        }
        return fallback();
    }

    public static bool TryGetResource(string key, out object? value)
    {
        value = null;
        var app = Application.Current;
        if (app is null)
            return false;
        return app.TryGetResource(key, app.ActualThemeVariant, out value)
               || app.TryGetResource(key, null, out value);
    }

    private static SolidColorBrush ParseBrush(string hex) => new(Avalonia.Media.Color.Parse(hex));
}
