using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Muses.Core.Chrome;

namespace Muses.App.Theme;

/// <summary>
/// Keeps App.axaml <c>MusesMotion*</c> TimeSpan resources in sync with
/// <see cref="MusesMotion"/> and zeros them when reduced motion is enabled.
/// Avalonia <see cref="Avalonia.Platform.IPlatformSettings"/> does not expose
/// PrefersReducedMotion, so we read the Windows accessibility setting directly.
/// </summary>
public static class MotionChrome
{
    public const string HoverKey = "MusesMotionHover";
    public const string OverlayKey = "MusesMotionOverlay";
    public const string DrawerKey = "MusesMotionDrawer";
    public const string NowPlayingMorphKey = "MusesMotionNowPlayingMorph";
    public const string CollectionDeckSnapKey = "MusesMotionCollectionDeckSnap";
    public const string CollectionListTransitionKey = "MusesMotionCollectionListTransition";
    public const string CollectionCardActivationKey = "MusesMotionCollectionCardActivation";

    public static bool PreferReducedMotion { get; private set; }

    public static TimeSpan HoverDuration =>
        MusesMotion.DurationOrZero(MusesMotion.HoverSeconds(PreferReducedMotion));

    public static TimeSpan OverlayDuration =>
        MusesMotion.DurationOrZero(MusesMotion.OverlaySeconds(PreferReducedMotion));

    public static TimeSpan DrawerDuration =>
        MusesMotion.DurationOrZero(MusesMotion.DrawerSeconds(PreferReducedMotion));

    public static TimeSpan MorphDuration =>
        MusesMotion.DurationOrZero(MusesMotion.MorphSeconds(PreferReducedMotion));

    public static TimeSpan CollectionDeckDuration =>
        MusesMotion.DurationOrZero(MusesMotion.CollectionDeckSeconds(PreferReducedMotion));

    public static TimeSpan CollectionListDuration =>
        MusesMotion.DurationOrZero(MusesMotion.CollectionListSeconds(PreferReducedMotion));

    public static TimeSpan CollectionCardDuration =>
        MusesMotion.DurationOrZero(MusesMotion.CollectionCardSeconds(PreferReducedMotion));

    /// <summary>
    /// Initialize the motion resources before the main window is created.
    /// </summary>
    public static void Apply(Application app) => Apply(app, DetectPreferReducedMotion());

    private static void Apply(Application app, bool reduced)
    {
        PreferReducedMotion = reduced;
        SyncResources(app.Resources);
    }

    internal static bool Refresh(Application app)
    {
        var reduced = DetectPreferReducedMotion();
        if (PreferReducedMotion == reduced) return false;
        Apply(app, reduced);
        return true;
    }

    public static void SyncResources(IResourceDictionary res)
    {
        res[HoverKey] = HoverDuration;
        res[OverlayKey] = OverlayDuration;
        res[DrawerKey] = DrawerDuration;
        res[NowPlayingMorphKey] = MorphDuration;
        res[CollectionDeckSnapKey] = CollectionDeckDuration;
        res[CollectionListTransitionKey] = CollectionListDuration;
        res[CollectionCardActivationKey] = CollectionCardDuration;
    }

    private static bool DetectPreferReducedMotion()
    {
        try
        {
            // SPI_GETCLIENTAREAANIMATION — false means the user disabled window animations.
            var enabled = true;
            if (SystemParametersInfo(0x1042 /* SPI_GETCLIENTAREAANIMATION */, 0, ref enabled, 0))
                return !enabled;
        }
        catch
        {
            // Fall through.
        }

        return false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, ref bool pvParam, uint fWinIni);
}
