using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Microsoft.Web.WebView2.Core;
using Muses.Core.YouTube;

namespace Muses.App.Services;

/// <summary>
/// Win32 HWND host for a CoreWebView2Controller. Used only inside YouTubeVideoOverlay.
/// </summary>
public sealed class WebView2NativeHost : NativeControlHost
{
    private IntPtr _hwnd;
    private CoreWebView2Controller? _controller;
    private string? _pendingHtml;
    private bool _destroying;
    private Size _lastSize;

    public bool IsControllerReady => _controller?.CoreWebView2 is not null;

    public void NavigateToHtml(string html)
    {
        _pendingHtml = html;
        if (_controller?.CoreWebView2 is { } webView)
        {
            try { webView.NavigateToString(html); }
            catch { /* best-effort */ }
        }
    }

    public void NavigateBlank()
    {
        NavigateToHtml(YouTubeEmbed.BlankHtml());
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
            return base.CreateNativeControlCore(parent);

        _hwnd = CreateWindowEx(
            0,
            "Static",
            "",
            WS_CHILD | WS_VISIBLE,
            0, 0, 1, 1,
            parent.Handle,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            return base.CreateNativeControlCore(parent);

        _ = InitControllerAsync();
        return new PlatformHandle(_hwnd, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        _destroying = true;
        try
        {
            if (_controller is not null)
            {
                try { _controller.Close(); } catch { /* ignore */ }
                _controller = null;
            }

            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }
        finally
        {
            base.DestroyNativeControlCore(control);
        }
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _lastSize = finalSize;
        ApplyBounds(finalSize);
        return base.ArrangeOverride(finalSize);
    }

    private async Task InitControllerAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync().ConfigureAwait(true);
            if (_destroying || _hwnd == IntPtr.Zero) return;

            var controller = await env.CreateCoreWebView2ControllerAsync(_hwnd).ConfigureAwait(true);
            if (_destroying)
            {
                try { controller.Close(); } catch { /* ignore */ }
                return;
            }

            _controller = controller;
            controller.DefaultBackgroundColor = System.Drawing.Color.Black;
            if (controller.CoreWebView2 is { } webView)
            {
                webView.Settings.AreDefaultContextMenusEnabled = false;
                webView.Settings.AreDevToolsEnabled = false;
                webView.Settings.IsStatusBarEnabled = false;
                if (!string.IsNullOrEmpty(_pendingHtml))
                    webView.NavigateToString(_pendingHtml);
            }

            ApplyBounds(_lastSize);
        }
        catch
        {
            // Host stays empty; overlay falls back to degraded messaging via IsAvailable checks.
        }
    }

    private void ApplyBounds(Size size)
    {
        if (_controller is null) return;
        var w = Math.Max(1, (int)Math.Ceiling(size.Width));
        var h = Math.Max(1, (int)Math.Ceiling(size.Height));
        try
        {
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, w, h);
            _controller.IsVisible = true;
        }
        catch
        {
            // ignore resize races during teardown
        }
    }

    private const int WS_CHILD = 0x40000000;
    private const int WS_VISIBLE = 0x10000000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        int dwExStyle,
        string lpClassName,
        string lpWindowName,
        int dwStyle,
        int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
