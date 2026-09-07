using Muses.Core.Chrome;

namespace Muses.Tests;

public class GlassSurfaceTests
{
    [Fact]
    public void Accessibility_off_and_supports_glass_is_glass()
    {
        Assert.Equal(GlassMode.Glass, GlassDecision.Mode(false, false, true));
    }

    [Fact]
    public void Accessibility_off_and_older_system_is_material()
    {
        Assert.Equal(GlassMode.Material, GlassDecision.Mode(false, false, false));
    }

    [Fact]
    public void Reduce_transparency_overrides_to_opaque()
    {
        Assert.Equal(GlassMode.Opaque, GlassDecision.Mode(true, false, true));
        Assert.Equal(GlassMode.Opaque, GlassDecision.Mode(true, false, false));
    }

    [Fact]
    public void Increase_contrast_overrides_to_opaque()
    {
        Assert.Equal(GlassMode.Opaque, GlassDecision.Mode(false, true, true));
        Assert.Equal(GlassMode.Opaque, GlassDecision.Mode(false, true, false));
    }

    [Fact]
    public void Both_accessibility_flags_are_opaque()
    {
        Assert.Equal(GlassMode.Opaque, GlassDecision.Mode(true, true, true));
    }

    [Fact]
    public void Player_and_compact_roles_are_interactive()
    {
        Assert.True(MusesGlassRole.Player.IsInteractive());
        Assert.True(MusesGlassRole.CompactControl.IsInteractive());
        Assert.False(MusesGlassRole.PersistentChrome.IsInteractive());
        Assert.False(MusesGlassRole.FloatingPanel.IsInteractive());
    }
}
