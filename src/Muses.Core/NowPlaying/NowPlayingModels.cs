namespace Muses.Core.NowPlaying;

public enum NowPlayingMode
{
    Cover,
    Vinyl
}

public enum NowPlayingLyricsMode
{
    Inline,
    Fullscreen
}

public enum NowPlayingPresentation
{
    Split,
    Stacked
}

public sealed record NowPlayingLayout(
    NowPlayingPresentation Presentation,
    double ContentWidth,
    double StageSide,
    double ArtworkSlotSide,
    double ArtworkScale,
    double ColumnGap,
    double LyricsLeadingInset
)
{
    public const double SplitBreakpoint = 1040.0;
    public const double ReferenceContentWidth = 1120.0;
    public const double ReferenceStageSide = 404.0;
    public const double LiveCoverPlayingScale = 1.06;

    public double RenderedArtworkSide => ArtworkSlotSide * ArtworkScale;

    public static NowPlayingLayout Resolve(double width, double height, bool isPlaying, bool reduceMotion = false)
    {
        var safeWidth = Math.Max(0, width);
        var safeHeight = Math.Max(0, height);
        var presentation = safeWidth >= SplitBreakpoint ? NowPlayingPresentation.Split : NowPlayingPresentation.Stacked;
        var artworkScale = reduceMotion ? 1.0 : (isPlaying ? LiveCoverPlayingScale : 1.0);

        if (presentation == NowPlayingPresentation.Split)
        {
            var contentWidth = Math.Min(ReferenceContentWidth, Math.Max(0, safeWidth - 300));
            var stageSide = Math.Min(ReferenceStageSide, Math.Max(292, safeHeight * 0.45));
            var gap = Math.Min(144, Math.Max(64, 64 + (safeWidth - SplitBreakpoint) * 0.2));
            var slotSide = stageSide / LiveCoverPlayingScale;
            var lyricsInset = safeWidth >= 1320 ? 30.0 : 18.0;

            return new NowPlayingLayout(
                Presentation: presentation,
                ContentWidth: contentWidth,
                StageSide: stageSide,
                ArtworkSlotSide: slotSide,
                ArtworkScale: artworkScale,
                ColumnGap: gap,
                LyricsLeadingInset: lyricsInset
            );
        }

        var stackedStage = Math.Min(360, Math.Max(248, Math.Min(safeWidth - 64, safeHeight * 0.58)));
        var stackedSlot = stackedStage / LiveCoverPlayingScale;

        return new NowPlayingLayout(
            Presentation: presentation,
            ContentWidth: Math.Max(0, safeWidth - 48),
            StageSide: stackedStage,
            ArtworkSlotSide: stackedSlot,
            ArtworkScale: artworkScale,
            ColumnGap: 0,
            LyricsLeadingInset: 0
        );
    }
}

public static class NowPlayingVolumePolicy
{
    public const float SilenceThreshold = 0.001f;
    public const float FallbackAudibleVolume = 0.8f;

    public static bool IsMuted(float volume) => volume <= SilenceThreshold;

    public static float RememberedAudibleVolume(float current, float previous)
    {
        if (IsMuted(current)) return previous;
        return Math.Clamp(current, 0f, 1f);
    }

    public static float ToggledVolume(float current, float remembered)
    {
        if (!IsMuted(current)) return 0f;
        var candidate = IsMuted(remembered) ? FallbackAudibleVolume : remembered;
        return Math.Clamp(candidate, 0f, 1f);
    }
}
