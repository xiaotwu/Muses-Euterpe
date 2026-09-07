using Muses.Core.Domain;
using Localization = global::Muses.Core.L10n.L10n;

namespace Muses.Core.Advanced;

public sealed record AudioStreamStats(
    string? Codec,
    bool IsLossless,
    int? SampleRate,
    int? BitDepth,
    int? BitRate,
    int? Channels,
    string Source,
    string? OutputDevice,
    double? ReplayGain,
    string? EQPresetId,
    double Volume,
    double? Lufs = null);

public sealed record AudioInfoRow(string Label, string Value);

public static class AudioInfoModel
{
    public const string Unknown = "Unknown";

    public static IReadOnlyList<AudioInfoRow> BuildRows(
        TrackSnapshot? track,
        string? outputDevice,
        string? eqPresetId,
        double volume)
    {
        var src = track == null ? Unknown : "YouTube";
        return
        [
            new(Localization.Tr("Codec", "编码"), track?.Codec ?? Unknown),
            new(Localization.Tr("Lossless", "无损"), track?.IsLossless == true ? Localization.Tr("Yes", "是") : (track != null ? Localization.Tr("No", "否") : Unknown)),
            new(Localization.Tr("Sample Rate", "采样率"), track?.SampleRate.HasValue == true ? $"{track.SampleRate.Value / 1000.0:0.#} kHz" : Unknown),
            new(Localization.Tr("Bit Depth", "位深"), track?.BitDepth.HasValue == true ? $"{track.BitDepth.Value}-bit" : Unknown),
            new(Localization.Tr("Bit Rate", "比特率"), track?.BitRate.HasValue == true ? $"{track.BitRate.Value / 1000} kbps" : Unknown),
            new(Localization.Tr("Channels", "声道"), track?.Channels switch
            {
                1 => Localization.Tr("Mono", "单声道"),
                2 => Localization.Tr("Stereo", "立体声"),
                null => Unknown,
                var c => $"{c}"
            }),
            new(Localization.Tr("Source", "来源"), src),
            new(Localization.Tr("Output Device", "输出设备"), outputDevice ?? Unknown),
            new(Localization.Tr("ReplayGain", "回放增益"), track?.ReplayGain.HasValue == true ? $"{track.ReplayGain.Value:+0.0;-0.0;0.0} dB" : Unknown),
            new(Localization.Tr("EQ Preset", "EQ 预设"), string.IsNullOrEmpty(eqPresetId) ? Unknown : eqPresetId),
            new(Localization.Tr("Volume", "音量"), $"{volume * 100:0}%")
        ];
    }
}
