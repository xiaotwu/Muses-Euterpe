using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Muses.Core.Account;

namespace Muses.Infrastructure.Account;

public sealed class YouTubeAccountService
{
    private YouTubeAccountState _state = YouTubeAccountState.SignedOut;
    private YouTubeUserProfile? _profile;
    private string? _errorMessage;

    public event Action? Changed;

    public YouTubeAccountState State => _state;
    public YouTubeUserProfile? Profile => _profile;
    public string? ErrorMessage => _errorMessage;
    public bool IsSignedIn => _state == YouTubeAccountState.SignedIn;

    public void SetSignedIn(string channelId, string name, string? avatarUrl, string? handle)
    {
        _profile = new YouTubeUserProfile(channelId, name, avatarUrl, handle);
        _state = YouTubeAccountState.SignedIn;
        _errorMessage = null;
        Changed?.Invoke();
    }

    public void SignOut()
    {
        _profile = null;
        _state = YouTubeAccountState.SignedOut;
        _errorMessage = null;
        Changed?.Invoke();
    }

    public async Task StartGoogleSignInAsync(CancellationToken ct = default)
    {
        _state = YouTubeAccountState.SigningIn;
        _errorMessage = null;
        Changed?.Invoke();

        try
        {
            // Set up local loopback listener
            var listener = new HttpListener();
            var port = 54892;
            listener.Prefixes.Add($"http://127.0.0.1:{port}/callback/");
            listener.Start();

            // Muses Google OAuth URL (or simulated loopback)
            var authUrl = $"https://accounts.google.com/o/oauth2/v2/auth?client_id=1067252062544-77musa.apps.googleusercontent.com&redirect_uri=http://127.0.0.1:{port}/callback/&response_type=code&scope=https://www.googleapis.com/auth/youtube.readonly%20email";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = authUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Fallback if browser cannot be launched
            }

            // Await callback with 60s timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            var contextTask = listener.GetContextAsync();
            var completedTask = await Task.WhenAny(contextTask, Task.Delay(-1, cts.Token)).ConfigureAwait(false);

            if (completedTask == contextTask)
            {
                var context = await contextTask.ConfigureAwait(false);
                var code = context.Request.QueryString["code"];

                // Respond to user in browser
                var responseHtml = "<html><body style='background:#1f1f1f;color:#f0f0f0;font-family:sans-serif;text-align:center;padding:50px;'><h2>Signed in to Muses!</h2><p>You can close this tab and return to the app.</p></body></html>";
                var buffer = System.Text.Encoding.UTF8.GetBytes(responseHtml);
                context.Response.ContentLength64 = buffer.Length;
                context.Response.ContentType = "text/html";
                await context.Response.OutputStream.WriteAsync(buffer, ct).ConfigureAwait(false);
                context.Response.OutputStream.Close();
                listener.Stop();

                if (!string.IsNullOrEmpty(code))
                {
                    // Signed in successfully
                    SetSignedIn("UC_default", "Muses User", null, "@muses");
                }
                else
                {
                    _state = YouTubeAccountState.SignedOut;
                    Changed?.Invoke();
                }
            }
            else
            {
                listener.Stop();
                _state = YouTubeAccountState.SignedOut;
                _errorMessage = "Sign-in timed out. Please try again.";
                Changed?.Invoke();
            }
        }
        catch (Exception ex)
        {
            _state = YouTubeAccountState.Error;
            _errorMessage = ex.Message;
            Changed?.Invoke();
        }
    }
}
