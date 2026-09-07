namespace Muses.Core.Chrome;

/// <summary>
/// Shared motion timings (seconds). AXAML maps these via App.axaml
/// <c>MusesMotion*</c> TimeSpan resources (synced by MotionChrome at startup).
/// </summary>
public static class MusesMotion
{
    public const double Hover = 0.15;
    public const double Overlay = 0.20;
    public const double Drawer = 0.25;
    public const double NowPlayingMorph = 0.32;
    public const double CollectionDeckSnap = 0.22;
    public const double CollectionListTransition = 0.29;
    public const double CollectionCardActivation = 0.30;

    public static TimeSpan HoverDuration => TimeSpan.FromSeconds(Hover);
    public static TimeSpan OverlayDuration => TimeSpan.FromSeconds(Overlay);
    public static TimeSpan DrawerDuration => TimeSpan.FromSeconds(Drawer);
    public static TimeSpan NowPlayingMorphDuration => TimeSpan.FromSeconds(NowPlayingMorph);
    public static TimeSpan CollectionDeckSnapDuration => TimeSpan.FromSeconds(CollectionDeckSnap);
    public static TimeSpan CollectionListTransitionDuration => TimeSpan.FromSeconds(CollectionListTransition);
    public static TimeSpan CollectionCardActivationDuration => TimeSpan.FromSeconds(CollectionCardActivation);

    public static double? HoverSeconds(bool reduceMotion) => reduceMotion ? null : Hover;
    public static double? MorphSeconds(bool reduceMotion) => reduceMotion ? null : NowPlayingMorph;
    public static double? DrawerSeconds(bool reduceMotion) => reduceMotion ? null : Drawer;
    public static double? OverlaySeconds(bool reduceMotion) => reduceMotion ? null : Overlay;
    public static double? CollectionDeckSeconds(bool reduceMotion) => reduceMotion ? null : CollectionDeckSnap;
    public static double? CollectionListSeconds(bool reduceMotion) => reduceMotion ? null : CollectionListTransition;
    public static double? CollectionCardSeconds(bool reduceMotion) => reduceMotion ? null : CollectionCardActivation;

    /// <summary>Avalonia-friendly duration; <see cref="TimeSpan.Zero"/> when reduce-motion helpers return null.</summary>
    public static TimeSpan DurationOrZero(double? seconds) =>
        seconds is { } s ? TimeSpan.FromSeconds(s) : TimeSpan.Zero;
}
