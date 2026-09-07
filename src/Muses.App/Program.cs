using Avalonia;
using Avalonia.Media;
using System;

namespace Muses.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
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
