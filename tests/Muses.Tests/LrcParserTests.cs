using Muses.Core.Lyrics;
using Xunit;

namespace Muses.Tests;

public class LrcParserTests
{
    [Fact]
    public void Parse_StandardLrc_ExtractsSortedLinesAndTimestamps()
    {
        var raw = """
            [ti:Test Song]
            [ar:Test Artist]
            [00:12.50]Line one text
            [00:18.00]Line two text
            [00:25.80]Line three text
            """;

        var (lines, offset) = LrcParser.Parse(raw);

        Assert.Equal(0, offset);
        Assert.Equal(3, lines.Count);
        Assert.Equal(12500, lines[0].TimeMs);
        Assert.Equal("Line one text", lines[0].Text);
        Assert.Equal(18000, lines[1].TimeMs);
        Assert.Equal("Line two text", lines[1].Text);
        Assert.Equal(25800, lines[2].TimeMs);
    }

    [Fact]
    public void Parse_WithOffsetHeader_AppliesOffset()
    {
        var raw = """
            [offset:500]
            [00:02.00]First line
            """;

        var (lines, detectedOffset) = LrcParser.Parse(raw);

        Assert.Equal(500, detectedOffset);
        Assert.Single(lines);
        Assert.Equal(2500, lines[0].TimeMs); // 2000 + 500
    }

    [Fact]
    public void Parse_WithManualOffset_AddsToTimestamps()
    {
        var raw = "[00:10.00]Hello world";
        var (lines, _) = LrcParser.Parse(raw, manualOffsetMs: -1000);

        Assert.Single(lines);
        Assert.Equal(9000, lines[0].TimeMs); // 10000 - 1000
    }

    [Fact]
    public void Parse_PlainLyrics_ReturnsUntimedLines()
    {
        var raw = """
            First plain line
            Second plain line
            """;

        var (lines, _) = LrcParser.Parse(raw);

        Assert.Equal(2, lines.Count);
        Assert.Null(lines[0].TimeMs);
        Assert.Equal("First plain line", lines[0].Text);
        Assert.Null(lines[1].TimeMs);
        Assert.Equal("Second plain line", lines[1].Text);
    }

    [Fact]
    public void FindCurrentLineIndex_ResolvesCorrectLine()
    {
        var raw = """
            [00:05.00]Five seconds
            [00:10.00]Ten seconds
            [00:20.00]Twenty seconds
            """;

        var (lines, _) = LrcParser.Parse(raw);

        // Before first line
        Assert.Equal(0, LrcParser.FindCurrentLineIndex(lines, 2000));
        // Exactly at first line
        Assert.Equal(0, LrcParser.FindCurrentLineIndex(lines, 5000));
        // Between first and second
        Assert.Equal(0, LrcParser.FindCurrentLineIndex(lines, 8000));
        // At second line
        Assert.Equal(1, LrcParser.FindCurrentLineIndex(lines, 10000));
        // Past third line
        Assert.Equal(2, LrcParser.FindCurrentLineIndex(lines, 30000));
    }
}
