using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Muses.App.Services;

internal sealed class LinuxWebKitYouTubeIFrameClient : IHostedYouTubeIFrameClient
{
    private const string ReadyMessage = "muses-video-ready";
    private const string ErrorPrefix = "muses-video-error:";
    private readonly Border _host = new();
    private NativeWebView? _view;
    private IDisposable? _readyTimeout;
    private bool? _runtimeInstalled;
    private bool _loadFailed;

    private LinuxWebKitYouTubeIFrameClient() { }

    public event EventHandler? AvailabilityChanged;
    public bool IsAvailable => !_loadFailed && (_runtimeInstalled ??= CheckRuntime());
    public bool IsLoaded => _view is not null;
    public bool RuntimeUnavailable => _runtimeInstalled == false;
    public Uri PageOrigin { get; } = new("https://xiaotwu.github.io/Project-Muses/");
    public Control HostControl => _host;

    public static LinuxWebKitYouTubeIFrameClient? TryCreate()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
            return null;
        return new LinuxWebKitYouTubeIFrameClient();
    }

    public void Load(string videoId, string pageHtml)
    {
        TearDown();
        if (!IsAvailable)
            return;
        var view = new NativeWebView
        {
            AdapterPreference = [WebViewAdapterType.WebKitGtk],
            Background = Brushes.Black,
        };
        view.EnvironmentRequested += (_, e) =>
        {
            if (e is GtkWebViewEnvironmentRequestedEventArgs gtk)
                gtk.EphemeralDataManager = true;
        };
        view.NewWindowRequested += (_, e) => e.Handled = true;
        view.WebMessageReceived += OnPlayerMessage;
        _view = view;
        _readyTimeout = DispatcherTimer.RunOnce(
            () => Fail(view, "YouTube player did not report readiness"), TimeSpan.FromSeconds(20));
        // Queue navigation until the adapter attaches to the visual tree.
        view.NavigateToString(pageHtml, PageOrigin);
        _host.Child = view;
    }

    public void TearDown()
    {
        _readyTimeout?.Dispose();
        _readyTimeout = null;
        if (_view is { } view)
        {
            view.WebMessageReceived -= OnPlayerMessage;
            _view = null;
            // Detaching destroys the adapter and its ephemeral session.
            _host.Child = null;
        }
        _loadFailed = false;
    }

    private static bool CheckRuntime() =>
        WebViewAdapterInfo.GetAdapterInfo(WebViewAdapterType.WebKitGtk).IsInstalled;

    private void OnPlayerMessage(object? sender, WebMessageReceivedEventArgs e)
    {
        if (sender is not NativeWebView view || !ReferenceEquals(view, _view) || e.Body is not string message)
            return;
        if (message == ReadyMessage)
        {
            _readyTimeout?.Dispose();
            _readyTimeout = null;
        }
        else if (message.StartsWith(ErrorPrefix, StringComparison.Ordinal) &&
                 int.TryParse(message.AsSpan(ErrorPrefix.Length), out var code))
        {
            Fail(view, $"YouTube IFrame error {code}");
        }
    }

    private void Fail(NativeWebView view, string reason)
    {
        if (!ReferenceEquals(view, _view))
            return;
        Trace.TraceWarning("Linux video overlay: {0}", reason);
        TearDown();
        _loadFailed = true;
        AvailabilityChanged?.Invoke(this, EventArgs.Empty);
    }
}
