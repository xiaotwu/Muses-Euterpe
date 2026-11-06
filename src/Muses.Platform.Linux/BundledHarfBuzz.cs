using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Muses.Platform.Linux;

public static class BundledHarfBuzz
{
    private const int ResolveNow = 0x2;
    private const int PreferLocalSymbols = 0x8;

    public static void Initialize()
    {
        if (!OperatingSystem.IsLinux()) return;

        // FreeType and GTK can load a different HarfBuzz globally. The bundled
        // library must resolve its own symbols before Avalonia shapes any text.
        // Remove this once the shipped Linux library includes SkiaSharp #5352.
        var searchDirectories = AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string
            ?? AppContext.BaseDirectory;
        foreach (var directory in searchDirectories.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!Path.IsPathFullyQualified(directory)) continue;

            var libraryPath = Path.Combine(directory, "libHarfBuzzSharp.so");
            if (!File.Exists(libraryPath)) continue;

            try
            {
                // glibc keeps this local handle open for the rest of the process.
                if (OpenLibrary(libraryPath, ResolveNow | PreferLocalSymbols) == IntPtr.Zero)
                    Trace.TraceWarning("Could not isolate the bundled Linux HarfBuzz library.");
            }
            catch (Exception error) when (error is DllNotFoundException or EntryPointNotFoundException)
            {
                // Non-glibc environments can use the standard .NET native loader.
            }

            return;
        }
    }

    [DllImport("libdl.so.2", EntryPoint = "dlopen")]
    private static extern IntPtr OpenLibrary([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
}
