namespace Muses.Core.Chrome;

public enum MusesGlassRole
{
    PersistentChrome,
    Player,
    FloatingPanel,
    CompactControl
}

public static class MusesGlassRoleExtensions
{
    public static bool IsInteractive(this MusesGlassRole role) =>
        role is MusesGlassRole.Player or MusesGlassRole.CompactControl;
}

public enum GlassMode
{
    Opaque,
    Glass,
    Material
}

/// <summary>
/// Windows 11: Glass = Mica/Acrylic. Older hosts / Skia: Material = translucent fill.
/// Reduce Transparency or Increase Contrast always forces Opaque.
/// </summary>
public static class GlassDecision
{
    public static GlassMode Mode(bool reduceTransparency, bool increaseContrast, bool supportsGlass)
    {
        if (reduceTransparency || increaseContrast) return GlassMode.Opaque;
        return supportsGlass ? GlassMode.Glass : GlassMode.Material;
    }
}
