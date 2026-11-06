namespace Muses.Core.Platform;

public static class MediaToolPaths
{
    public static string? FindPackaged(string tool, string? applicationDirectory = null)
    {
        var root = new DirectoryInfo(applicationDirectory ?? AppContext.BaseDirectory);
        if (root.Name.Equals("Helpers", StringComparison.OrdinalIgnoreCase) && root.Parent is { } parent)
            root = parent;

        var name = OperatingSystem.IsWindows() ? tool + ".exe" : tool;
        var path = Path.Combine(root.FullName, "MediaTools", name);
        return File.Exists(path) ? path : null;
    }
}
