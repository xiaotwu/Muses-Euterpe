namespace Muses.Core.Chrome;

/// <summary>
/// Apple Music near-black in dark mode and a restrained light palette.
/// RGB channels are 0–1. Appearance is applied by the UI host.
/// </summary>
public readonly record struct Rgba(double R, double G, double B, double A = 1.0)
{
    public byte ByteR => ToByte(R);
    public byte ByteG => ToByte(G);
    public byte ByteB => ToByte(B);
    public byte ByteA => ToByte(A);

    public string ToHexRgb() =>
        $"{ByteR:X2}{ByteG:X2}{ByteB:X2}";

    public static Rgba FromRgb(double r, double g, double b, double a = 1.0) => new(r, g, b, a);

    public static Rgba FromBytes(byte r, byte g, byte b, byte a = 255) =>
        new(r / 255.0, g / 255.0, b / 255.0, a / 255.0);

    private static byte ToByte(double channel) =>
        (byte)Math.Clamp((int)Math.Round(channel * 255.0), 0, 255);
}

public readonly record struct ThemePair(Rgba Dark, Rgba Light)
{
    public Rgba For(bool isDark) => isDark ? Dark : Light;
}

public static class BrandColors
{
    public static readonly ThemePair Background = new(
        Rgba.FromRgb(AppleMusicTokens.DarkPageRgb.R, AppleMusicTokens.DarkPageRgb.G, AppleMusicTokens.DarkPageRgb.B),
        Rgba.FromRgb(AppleMusicTokens.LightPageRgb.R, AppleMusicTokens.LightPageRgb.G, AppleMusicTokens.LightPageRgb.B));

    public static readonly ThemePair Surface = new(
        Rgba.FromRgb(0.15, 0.15, 0.17),
        Rgba.FromRgb(0.92, 0.92, 0.94));

    public static readonly ThemePair Magenta = new(
        Rgba.FromRgb(AppleMusicTokens.KeyColorRgb.R, AppleMusicTokens.KeyColorRgb.G, AppleMusicTokens.KeyColorRgb.B),
        Rgba.FromRgb(AppleMusicTokens.KeyColorRgb.R, AppleMusicTokens.KeyColorRgb.G, AppleMusicTokens.KeyColorRgb.B));

    public static readonly ThemePair TextPrimary = new(
        Rgba.FromRgb(0.94, 0.94, 0.94),
        Rgba.FromRgb(0.09, 0.09, 0.10));

    public static readonly ThemePair TextSecondary = new(
        Rgba.FromRgb(0.65, 0.65, 0.68),
        Rgba.FromRgb(0.45, 0.45, 0.48));

    public static readonly ThemePair Hairline = new(
        Rgba.FromRgb(1, 1, 1, 0.10),
        Rgba.FromRgb(0, 0, 0, 0.08));

    public static readonly ThemePair Scrim = new(
        Rgba.FromRgb(0, 0, 0, 0.35),
        Rgba.FromRgb(0, 0, 0, 0.25));

    /// <summary>YouTube brand red. Never used for play/pause.</summary>
    public static readonly Rgba YouTubeRed = Rgba.FromRgb(1, 0, 0);
}
