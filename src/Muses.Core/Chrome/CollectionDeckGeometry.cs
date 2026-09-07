namespace Muses.Core.Chrome;

public enum CollectionPageMode
{
    Stage,
    List
}

public enum CollectionExpansionDirection
{
    Up,
    Down
}

public enum CollectionDeckActivationSource
{
    Pointer,
    Keyboard,
    Accessibility,
    ContextMenu
}

public enum ArtworkPresentation
{
    Fill,
    FitOnAmbient
}

public static class CollectionDeckActivationPolicy
{
    public static bool IsPrimaryActivation(CollectionDeckActivationSource source) =>
        source != CollectionDeckActivationSource.ContextMenu;
}

public static class CollectionDeckInputPolicy
{
    public static bool AcceptsEvents(bool stageEnabled, bool environmentEnabled) =>
        stageEnabled && environmentEnabled;
}

public static class CollectionDeckScrubberMetrics
{
    public const double MaximumWidth = 460;
    public const double MinimumWidth = 160;
    public const double HorizontalClearance = 220;
    public const double StageClearance = 24;
    public const double TrackHeight = 4;
    public const double ThumbWidth = 46;
    public const double ThumbHeight = 24;
    public const double ControlHeight = 28;
    public const double ValueHeight = 16;
    public const double ValueSpacing = 4;
    public const double TotalHeight = ControlHeight + ValueSpacing + ValueHeight;
    public const double PlayerClearance = OverlayChromeMetrics.ScrollBottomInset + 24;

    public static double Width(double availableWidth) =>
        Math.Min(MaximumWidth, Math.Max(MinimumWidth, availableWidth - HorizontalClearance));

    public static double ThumbCenterX(double position, int itemCount, double width)
    {
        var halfThumb = ThumbWidth / 2.0;
        var availableTravel = Math.Max(0, width - ThumbWidth);
        if (itemCount <= 1) return halfThumb;
        var ratio = Math.Min(1.0, Math.Max(0.0, position / (itemCount - 1)));
        return halfThumb + ratio * availableTravel;
    }

    public static double Position(double locationX, int itemCount, double width)
    {
        if (itemCount <= 1) return 0;
        var availableTravel = Math.Max(1.0, width - ThumbWidth);
        var ratio = Math.Min(1.0, Math.Max(0.0, (locationX - ThumbWidth / 2.0) / availableTravel));
        return ratio * (itemCount - 1);
    }
}

public sealed record CollectionDeckGeometry(
    double CardWidth,
    double FooterHeight,
    double Spread,
    int Radius)
{
    public double CardHeight => CardWidth + FooterHeight;

    public double LowerFanClearance => Radius switch
    {
        >= 4 => 118,
        3 => 92,
        _ => 66
    };

    public double ViewportHeight => CardHeight + LowerFanClearance;

    public static CollectionDeckGeometry Resolve(double containerWidth, double containerHeight)
    {
        var compactHeight = containerHeight < AppleMusicTokens.CollectionDeckCompactHeight;
        if (containerWidth < AppleMusicTokens.CollectionDeckCompactBreakpoint || compactHeight)
        {
            return new CollectionDeckGeometry(
                AppleMusicTokens.CollectionDeckCompactCardWidth,
                AppleMusicTokens.CollectionDeckCompactFooterHeight,
                AppleMusicTokens.CollectionDeckCompactSpread,
                2);
        }

        if (containerWidth < AppleMusicTokens.CollectionDeckWideBreakpoint)
        {
            return new CollectionDeckGeometry(
                Math.Min(146, Math.Max(136, containerWidth * 0.19)),
                AppleMusicTokens.CollectionDeckRoomyFooterHeight,
                AppleMusicTokens.CollectionDeckMediumSpread,
                3);
        }

        return new CollectionDeckGeometry(
            AppleMusicTokens.CollectionDeckRoomyCardWidth,
            AppleMusicTokens.CollectionDeckRoomyFooterHeight,
            AppleMusicTokens.CollectionDeckRoomySpread,
            4);
    }
}

public static class CollectionDeckProjection
{
    public static IReadOnlyList<int> VisibleIndices(int count, double position, int radius)
    {
        if (count <= 0) return [];
        var center = Math.Min(count - 1, Math.Max(0, (int)Math.Round(position)));
        var lower = Math.Max(0, center - radius);
        var upper = Math.Min(count - 1, center + radius);
        var list = new List<int>(upper - lower + 1);
        for (var i = lower; i <= upper; i++) list.Add(i);
        return list;
    }

    public static int ProjectedIndex(
        double startPosition,
        double translation,
        double predictedTranslation,
        double spread,
        int count)
    {
        if (count <= 0 || spread <= 0) return 0;
        var actual = startPosition - translation / spread;
        var projected = startPosition - predictedTranslation / spread;
        const double maximumFlight = 6;
        var boundedProjection = Math.Min(
            actual + maximumFlight,
            Math.Max(actual - maximumFlight, projected));
        return Math.Min(count - 1, Math.Max(0, (int)Math.Round(boundedProjection)));
    }

    public static bool AcceptsVerticalGesture(
        double translationWidth,
        double translationHeight,
        CollectionExpansionDirection direction,
        double threshold = AppleMusicTokens.CollectionDeckExpansionThreshold)
    {
        var multiplier = direction == CollectionExpansionDirection.Up ? -1.0 : 1.0;
        var directedY = translationHeight * multiplier;
        return directedY >= threshold
               && Math.Abs(translationHeight) > Math.Abs(translationWidth) * 1.25;
    }
}
