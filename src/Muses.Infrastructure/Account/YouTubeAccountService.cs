using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Muses.Core.Account;
using Muses.Core.L10n;
using Muses.Core.Platform;
using Muses.Infrastructure.Platform;

namespace Muses.Infrastructure.Account;

public sealed class YouTubeAccountService
{
    public const string TokensAccount = "tokens";
    public const string ProfileAccount = "profile";

    private readonly ICredentialStore _credentials;
    private readonly Func<GoogleOAuthConfig?> _configProvider;
    private readonly HttpMessageHandler? _handler;
    private readonly bool _allowLiveNetwork;

    private YouTubeAccountState _state = YouTubeAccountState.SignedOut;
    private YouTubeUserProfile? _profile;
    private string? _errorMessage;

    public event Action? Changed;

    public YouTubeAccountState State => _state;
    public YouTubeUserProfile? Profile => _profile;
    public string? ErrorMessage => _errorMessage;
    public bool IsSignedIn => _state == YouTubeAccountState.SignedIn;

    public YouTubeAccountService(
        ICredentialStore? credentials = null,
        Func<GoogleOAuthConfig?>? configProvider = null,
        HttpMessageHandler? handler = null,
        bool allowLiveNetwork = true)
    {
        _credentials = credentials ?? PlatformCredentialStore.CreateDefault();
        _configProvider = configProvider ?? (() => GoogleOAuthConfig.FromEnvironment());
        _handler = handler;
        _allowLiveNetwork = allowLiveNetwork;
        TryRestoreFromStore();
    }

    /// <summary>Test/helper: set signed-in profile without OAuth. Does not invent a fake production user.</summary>
    public void SetSignedIn(string channelId, string name, string? avatarUrl, string? handle)
    {
        _profile = new YouTubeUserProfile(channelId, name, avatarUrl, handle);
        _state = YouTubeAccountState.SignedIn;
        _errorMessage = null;
        PersistProfile(_profile);
        Changed?.Invoke();
    }

    public void SignOut()
    {
        _credentials.Delete(TokensAccount);
        _credentials.Delete(ProfileAccount);
        _profile = null;
        _state = YouTubeAccountState.SignedOut;
        _errorMessage = null;
        Changed?.Invoke();
    }

    public async Task StartGoogleSignInAsync(CancellationToken ct = default)
    {
        var config = _configProvider();
        if (config is null)
        {
            SetError(L10n.Tr(
                "This Muses build is missing its YouTube sign-in configuration. Set MUSES_GOOGLE_OAUTH_CLIENT_ID (and optional secret/redirect) or provide an untracked config.",
                "此 Muses 构建缺少 YouTube 登录配置。请设置 MUSES_GOOGLE_OAUTH_CLIENT_ID（以及可选的 secret/redirect），或提供未跟踪的配置。"));
            return;
        }

        if (!_allowLiveNetwork)
        {
            SetError(L10n.Tr(
                "Live Google sign-in is disabled in this environment.",
                "当前环境已禁用实时 Google 登录。"));
            return;
        }

        _state = YouTubeAccountState.SigningIn;
        _errorMessage = null;
        Changed?.Invoke();

        try
        {
            var verifier = CreateCodeVerifier();
            var challenge = CreateCodeChallenge(verifier);
            var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
            var port = config.LoopbackPort;
            var redirect = config.RedirectUri.TrimEnd('/') + "/";
            if (!redirect.EndsWith('/')) redirect += "/";

            using var listener = new HttpListener();
            var prefix = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(prefix);
            listener.Start();

            var authUrl = BuildAuthUrl(config, challenge, state, redirect);

            try
            {
                Process.Start(new ProcessStartInfo { FileName = authUrl, UseShellExecute = true });
            }
            catch
            {
                // Browser launch may fail in headless/CI; caller still waits for callback or timeout.
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(120));

            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                listener.Stop();
                SetError(L10n.Tr("Sign-in timed out. Please try again.", "登录超时，请重试。"));
                return;
            }

            var query = context.Request.QueryString;
            var returnedState = query["state"];
            var code = query["code"];
            var error = query["error"];

            var responseHtml = "<html><body style='background:#1f1f1f;color:#f0f0f0;font-family:sans-serif;text-align:center;padding:50px;'><h2>Signed in to Muses!</h2><p>You can close this tab and return to the app.</p></body></html>";
            var buffer = Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentLength64 = buffer.Length;
            context.Response.ContentType = "text/html";
            await context.Response.OutputStream.WriteAsync(buffer, ct).ConfigureAwait(false);
            context.Response.OutputStream.Close();
            listener.Stop();

            if (!string.IsNullOrEmpty(error))
            {
                SetError(L10n.Tr($"OAuth authorization failed: {error}", $"OAuth 授权失败: {error}"));
                return;
            }

            if (!string.Equals(returnedState, state, StringComparison.Ordinal))
            {
                SetError(L10n.Tr("OAuth authorization failed: state mismatch", "OAuth 授权失败: state 不匹配"));
                return;
            }

            if (string.IsNullOrEmpty(code))
            {
                _state = YouTubeAccountState.SignedOut;
                Changed?.Invoke();
                return;
            }

            var tokens = await ExchangeCodeAsync(config, code, verifier, redirect, ct).ConfigureAwait(false);
            PersistTokens(tokens);
            var profile = await FetchProfileAsync(tokens.AccessToken, ct).ConfigureAwait(false);
            _profile = profile;
            PersistProfile(profile);
            _state = YouTubeAccountState.SignedIn;
            _errorMessage = null;
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    public YouTubeAuthTokens? LoadTokens()
    {
        var raw = _credentials.Get(TokensAccount);
        if (raw is null) return null;
        try
        {
            return JsonSerializer.Deserialize<YouTubeAuthTokens>(raw);
        }
        catch
        {
            return null;
        }
    }

    private void TryRestoreFromStore()
    {
        var tokens = LoadTokens();
        if (tokens is null) return;
        var raw = _credentials.Get(ProfileAccount);
        if (raw is not null)
        {
            try
            {
                _profile = JsonSerializer.Deserialize<YouTubeUserProfile>(raw);
            }
            catch
            {
                _profile = null;
            }
        }
        if (_profile is not null)
        {
            _state = YouTubeAccountState.SignedIn;
        }
    }

    private void PersistTokens(YouTubeAuthTokens tokens)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(tokens);
        _credentials.Set(TokensAccount, bytes);
    }

    private void PersistProfile(YouTubeUserProfile profile)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(profile);
        _credentials.Set(ProfileAccount, bytes);
    }

    private void SetError(string message)
    {
        _state = YouTubeAccountState.Error;
        _errorMessage = message;
        Changed?.Invoke();
    }

    private HttpClient CreateClient()
    {
        return _handler is null ? new HttpClient() : new HttpClient(_handler, disposeHandler: false);
    }

    private static string BuildAuthUrl(GoogleOAuthConfig config, string challenge, string state, string redirect)
    {
        var scopes = config.Scopes.Count == 0 ? GoogleOAuthConfig.DefaultScopes : config.Scopes;
        var q = new Dictionary<string, string>
        {
            ["client_id"] = config.ClientId,
            ["redirect_uri"] = redirect.TrimEnd('/'),
            ["response_type"] = "code",
            ["scope"] = string.Join(' ', scopes),
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["access_type"] = "offline",
            ["prompt"] = "consent"
        };
        // Prefer trailing slash consistency with registered redirect
        q["redirect_uri"] = config.RedirectUri;
        return "https://accounts.google.com/o/oauth2/v2/auth?" + Encode(q);
    }

    private async Task<YouTubeAuthTokens> ExchangeCodeAsync(
        GoogleOAuthConfig config, string code, string verifier, string redirect, CancellationToken ct)
    {
        using var client = CreateClient();
        var body = new Dictionary<string, string>
        {
            ["code"] = code,
            ["client_id"] = config.ClientId,
            ["redirect_uri"] = config.RedirectUri,
            ["code_verifier"] = verifier,
            ["grant_type"] = "authorization_code"
        };
        if (!string.IsNullOrEmpty(config.ClientSecret))
            body["client_secret"] = config.ClientSecret;

        using var content = new FormUrlEncodedContent(body);
        using var resp = await client.PostAsync("https://oauth2.googleapis.com/token", content, ct).ConfigureAwait(false);
        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(L10n.Tr(
                $"OAuth token exchange failed: HTTP {(int)resp.StatusCode}",
                $"OAuth 令牌交换失败: HTTP {(int)resp.StatusCode}"));

        var parsed = JsonSerializer.Deserialize<TokenResponse>(raw)
                     ?? throw new InvalidOperationException(L10n.Tr(
                         "OAuth token exchange failed: parse error",
                         "OAuth 令牌交换失败: 解析错误"));
        if (string.IsNullOrEmpty(parsed.AccessToken))
            throw new InvalidOperationException(L10n.Tr(
                "OAuth token exchange failed: empty access token",
                "OAuth 令牌交换失败: access token 为空"));

        return new YouTubeAuthTokens(
            parsed.AccessToken,
            parsed.RefreshToken,
            DateTimeOffset.UtcNow.AddSeconds(parsed.ExpiresIn <= 0 ? 3600 : parsed.ExpiresIn),
            parsed.Scope);
    }

    private async Task<YouTubeUserProfile> FetchProfileAsync(string accessToken, CancellationToken ct)
    {
        using var client = CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var resp = await client.SendAsync(req, ct).ConfigureAwait(false);
        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException(L10n.Tr(
                $"Failed to load YouTube channel: HTTP {(int)resp.StatusCode}",
                $"无法加载 YouTube 频道: HTTP {(int)resp.StatusCode}"));

        using var doc = JsonDocument.Parse(raw);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.GetArrayLength() == 0)
            throw new InvalidOperationException(L10n.Tr(
                "No YouTube channel found for this Google account.",
                "此 Google 账号没有 YouTube 频道。"));

        var item = items[0];
        var id = item.GetProperty("id").GetString() ?? "";
        var snippet = item.GetProperty("snippet");
        var title = snippet.GetProperty("title").GetString() ?? id;
        string? avatar = null;
        if (snippet.TryGetProperty("thumbnails", out var thumbs) &&
            thumbs.TryGetProperty("default", out var def) &&
            def.TryGetProperty("url", out var url))
        {
            avatar = url.GetString();
        }
        string? handle = null;
        if (snippet.TryGetProperty("customUrl", out var custom))
            handle = custom.GetString();

        return new YouTubeUserProfile(id, title, avatar, handle);
    }

    private static string CreateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64Url(bytes);
    }

    private static string CreateCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64Url(hash);
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Encode(Dictionary<string, string> pairs) =>
        string.Join('&', pairs.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("scope")] public string? Scope { get; set; }
    }
}
