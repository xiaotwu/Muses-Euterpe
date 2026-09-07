using System.Text.RegularExpressions;

namespace Muses.Core.Lyrics;

public enum LyricsSource
{
    Lrclib,
    Musixmatch,
    Cached
}

public sealed record LyricWord(string Text, double StartMs, double? EndMs = null);

public sealed record LyricLine(
    int Index,
    double? TimeMs,
    string Text,
    string? Translation = null,
    IReadOnlyList<LyricWord>? Words = null
);

public sealed record LyricsResult(
    string? PlainLyrics,
    string? SyncedLyrics,
    LyricsSource Source,
    int? OffsetMs = null
);

public static class LrcParser
{
    private static readonly Regex TimestampRegex = new(@"\[(\d{1,2}):(\d{1,2})(?:[\.:](\d{1,3}))?\]", RegexOptions.Compiled);
    private static readonly Regex OffsetRegex = new(@"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static (IReadOnlyList<LyricLine> Lines, int DetectedOffsetMs) Parse(string? rawLrc, int manualOffsetMs = 0)
    {
        if (string.IsNullOrWhiteSpace(rawLrc))
            return (Array.Empty<LyricLine>(), 0);

        var detectedOffsetMs = 0;
        var linesWithTime = new List<(double TimeMs, string Text)>();
        var plainLines = new List<string>();

        var rawLines = rawLrc.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

        foreach (var rawLine in rawLines)
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Check for [offset:±ms] tag
            var offsetMatch = OffsetRegex.Match(trimmed);
            if (offsetMatch.Success && int.TryParse(offsetMatch.Groups[1].Value, out var off))
            {
                detectedOffsetMs = off;
                continue;
            }

            // Skip ID3/LRC header metadata tags like [ti:], [ar:], [al:], [by:]
            if (trimmed.StartsWith("[") && trimmed.Contains(":") && !char.IsDigit(trimmed[1]))
            {
                continue;
            }

            var matches = TimestampRegex.Matches(trimmed);
            if (matches.Count > 0)
            {
                // Remove timestamps from the text
                var cleanText = TimestampRegex.Replace(trimmed, "").Trim();

                foreach (Match match in matches)
                {
                    if (int.TryParse(match.Groups[1].Value, out var minutes) &&
                        int.TryParse(match.Groups[2].Value, out var seconds))
                    {
                        var fraction = 0;
                        if (match.Groups[3].Success)
                        {
                            var fracStr = match.Groups[3].Value;
                            if (fracStr.Length == 2 && int.TryParse(fracStr, out var cs))
                            {
                                fraction = cs * 10; // centiseconds to ms
                            }
                            else if (int.TryParse(fracStr, out var ms))
                            {
                                fraction = ms;
                            }
                        }

                        var totalMs = (minutes * 60 + seconds) * 1000.0 + fraction;
                        linesWithTime.Add((totalMs, cleanText));
                    }
                }
            }
            else
            {
                plainLines.Add(trimmed);
            }
        }

        var effectiveOffset = detectedOffsetMs + manualOffsetMs;

        if (linesWithTime.Count > 0)
        {
            // Sort by time
            var sorted = linesWithTime
                .OrderBy(l => l.TimeMs)
                .Select((l, index) => new LyricLine(
                    Index: index,
                    TimeMs: Math.Max(0, l.TimeMs + effectiveOffset),
                    Text: l.Text
                ))
                .ToList();

            return (sorted, detectedOffsetMs);
        }

        // Plain lines fallback (no timestamps)
        var plain = plainLines
            .Select((text, index) => new LyricLine(Index: index, TimeMs: null, Text: text))
            .ToList();

        return (plain, 0);
    }

    public static int FindCurrentLineIndex(IReadOnlyList<LyricLine> lines, double positionMs)
    {
        if (lines.Count == 0) return -1;

        var timedLines = lines.Where(l => l.TimeMs.HasValue).ToList();
        if (timedLines.Count == 0) return -1;

        var bestIndex = -1;
        for (var i = 0; i < timedLines.Count; i++)
        {
            if (timedLines[i].TimeMs <= positionMs)
            {
                bestIndex = timedLines[i].Index;
            }
            else
            {
                break;
            }
        }

        return bestIndex >= 0 ? bestIndex : timedLines[0].Index;
    }
}
