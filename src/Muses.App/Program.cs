using Avalonia;
using Avalonia.Media;
using Muses.App.Services;

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

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseHarfBuzz()
            .UseWin32()
            .UseSkia()
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
