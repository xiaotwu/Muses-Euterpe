using Muses.Core.NowPlaying;
using Xunit;

namespace Muses.Tests;

public class NowPlayingPolicyTests
{
    [Fact]
    public void Layout_ResolvesSplitPresentation_WhenWidthAtOrAbove1040()
    {
        var layout = NowPlayingLayout.Resolve(width: 1200, height: 800, isPlaying: true);

        Assert.Equal(NowPlayingPresentation.Split, layout.Presentation);
        Assert.Equal(NowPlayingLayout.LiveCoverPlayingScale, layout.ArtworkScale);
        Assert.True(layout.StageSide > 0);
        Assert.True(layout.ContentWidth > 0);
    }

    [Fact]
    public void Layout_ResolvesStackedPresentation_WhenWidthBelow1040()
    {
        var layout = NowPlayingLayout.Resolve(width: 800, height: 700, isPlaying: true);

        Assert.Equal(NowPlayingPresentation.Stacked, layout.Presentation);
        Assert.Equal(0, layout.ColumnGap);
    }

    [Fact]
    public void Layout_HonorsReduceMotion_OrPausedState()
    {
        var paused = NowPlayingLayout.Resolve(width: 1200, height: 800, isPlaying: false);
        Assert.Equal(1.0, paused.ArtworkScale);

        var reduced = NowPlayingLayout.Resolve(width: 1200, height: 800, isPlaying: true, reduceMotion: true);
        Assert.Equal(1.0, reduced.ArtworkScale);
    }

    [Fact]
    public void VolumePolicy_ToggledVolume_MutesAndRestores()
    {
        // When audible, toggling mutes to 0
        Assert.Equal(0f, NowPlayingVolumePolicy.ToggledVolume(0.75f, 0.75f));

        // When muted, toggling restores remembered volume
        Assert.Equal(0.75f, NowPlayingVolumePolicy.ToggledVolume(0f, 0.75f));

        // When muted and remembered is also 0, restores fallback 0.8
        Assert.Equal(NowPlayingVolumePolicy.FallbackAudibleVolume, NowPlayingVolumePolicy.ToggledVolume(0f, 0f));
    }
}
