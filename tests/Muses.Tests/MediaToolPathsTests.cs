using Muses.Core.Platform;
using Xunit;

namespace Muses.Tests;

public sealed class MediaToolPathsTests
{
    [Fact]
    public void App_and_isolated_helper_find_the_same_packaged_tool()
    {
        var root = Directory.CreateTempSubdirectory("muses-tool-paths-");
        var tools = root.CreateSubdirectory("MediaTools");
        var helper = root.CreateSubdirectory("Helpers");
        var executable = Path.Combine(tools.FullName, "yt-dlp.exe");
        try
        {
            File.WriteAllBytes(executable, []);
            Assert.Equal(executable, MediaToolPaths.FindPackaged("yt-dlp", root.FullName));
            Assert.Equal(executable, MediaToolPaths.FindPackaged("yt-dlp", helper.FullName));
            Assert.Null(MediaToolPaths.FindPackaged("deno", helper.FullName));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
