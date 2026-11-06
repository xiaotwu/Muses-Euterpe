using Avalonia;
using Avalonia.Media;
using Muses.App.Services;
using Muses.Platform.Linux;
using System;

namespace Muses.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.FirstOrDefault() == PackagePlaybackSmoke.Argument && PackagePlaybackSmoke.Parse(args) is null)
            return 2;
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        BundledHarfBuzz.Initialize();
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "fonts:Inter#Inter",
                FontFallbacks =
                [
                    new FontFallback { FontFamily = new FontFamily("Segoe UI Variable") },
                    new FontFallback { FontFamily = new FontFamily("Segoe UI") },
                    new FontFallback { FontFamily = new FontFamily("sans-serif") },
                ],
            })
            .LogToTrace();
    }
}
