namespace Muses.Core.Advanced;

/// <summary>
/// Real output-device probe. Never invents names like "Realtek…"; returns null when unknown.
/// </summary>
public interface IAudioOutputProbe
{
    /// <summary>Friendly name of the current default render device, or null.</summary>
    string? DefaultDeviceName { get; }

    /// <summary>Engine/device period in milliseconds, or null when not measurable.</summary>
    double? LatencyMilliseconds { get; }
}

public sealed class NullAudioOutputProbe : IAudioOutputProbe
{
    public static NullAudioOutputProbe Instance { get; } = new();
    public string? DefaultDeviceName => null;
    public double? LatencyMilliseconds => null;
}
