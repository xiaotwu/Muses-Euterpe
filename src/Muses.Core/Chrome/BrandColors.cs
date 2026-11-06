namespace Muses.Core.Chrome;

/// <summary>
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
        Rgba.FromBytes(0x21, 0x1F, 0x1B),
        Rgba.FromBytes(0xFC, 0xFA, 0xF6));

    public static readonly ThemePair Surface = new(
        Rgba.FromBytes(0x30, 0x2B, 0x23),
        Rgba.FromBytes(0xF0, 0xEC, 0xE4));

    public static readonly ThemePair Accent = new(
        Rgba.FromBytes(0xDF, 0xC2, 0x8B),
        Rgba.FromBytes(0x86, 0x63, 0x2E));

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
