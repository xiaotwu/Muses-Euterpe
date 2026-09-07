namespace Muses.Core.Advanced;

public enum TimeBand
{
    Morning,
    Afternoon,
    Evening,
    LateNight
}

public sealed record ListeningContext(
    int Hour,
    int DayOfWeek,
    bool IsWeekend,
    string? FrontmostAppBundleId,
    string? OutputDeviceName,
    bool? IsHeadphones)
{
    public TimeBand TimeBand => Hour switch
    {
        >= 5 and < 12 => TimeBand.Morning,
        >= 12 and < 18 => TimeBand.Afternoon,
        >= 18 and < 22 => TimeBand.Evening,
        _ => TimeBand.LateNight
    };

    public static ListeningContext CaptureCurrent(
        string? activeApp = null,
        string? deviceName = null,
        DateTimeOffset? now = null)
    {
        var dt = now ?? DateTimeOffset.Now;
        var hour = dt.Hour;
        var dow = (int)dt.DayOfWeek; // 0=Sunday..6=Saturday
        var isWeekend = dt.DayOfWeek is global::System.DayOfWeek.Saturday or global::System.DayOfWeek.Sunday;
        var isHeadphones = deviceName != null &&
            (deviceName.Contains("headphone", StringComparison.OrdinalIgnoreCase) ||
             deviceName.Contains("earphone", StringComparison.OrdinalIgnoreCase) ||
             deviceName.Contains("airpod", StringComparison.OrdinalIgnoreCase) ||
             deviceName.Contains("buds", StringComparison.OrdinalIgnoreCase));

        return new ListeningContext(hour, dow, isWeekend, activeApp, deviceName, isHeadphones);
    }
}
