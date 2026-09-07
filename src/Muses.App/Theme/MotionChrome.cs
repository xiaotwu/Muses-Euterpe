using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Muses.Core.Chrome;

namespace Muses.App.Theme;

/// <summary>
/// Keeps App.axaml <c>MusesMotion*</c> TimeSpan resources in sync with
/// <see cref="MusesMotion"/> and zeros them when the OS asks for reduced motion.
/// Avalonia 11.3 <see cref="Avalonia.Platform.IPlatformSettings"/> does not expose
/// PrefersReducedMotion, so we read platform accessibility settings directly.
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
    /// Call once after App.axaml resources load and before the main window is created
    /// so StaticResource durations resolve to the reduce-motion-aware values.
    /// </summary>
    public static void Apply(Application app)
    {
        PreferReducedMotion = DetectPreferReducedMotion();
        SyncResources(app.Resources);
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

    public static bool DetectPreferReducedMotion()
    {
        if (OperatingSystem.IsMacOS())
            return DetectMacOSReduceMotion();
        if (OperatingSystem.IsWindows())
            return DetectWindowsReduceMotion();
        return false;
    }

    private static bool DetectMacOSReduceMotion()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/defaults",
                ArgumentList = { "read", "com.apple.universalaccess", "reduceMotion" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process is null)
                return false;

            var output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(1500))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return false;
            }

            return output is "1" or "true" or "TRUE";
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectWindowsReduceMotion()
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
