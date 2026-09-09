namespace Muses.Core.Account;

/// <summary>
/// Application-owned Google Desktop OAuth configuration.
/// Production builds inject the client id via env / untracked config / user settings.
/// Never hard-code a production client_id in source.
/// </summary>
public sealed record GoogleOAuthConfig(
    string ClientId,
    string ClientSecret,
    string RedirectUri,
    IReadOnlyList<string> Scopes)
{
    public const string ReadOnlyScope = "https://www.googleapis.com/auth/youtube.readonly";
    public static readonly IReadOnlyList<string> DefaultScopes = [ReadOnlyScope];
    public const string DefaultRedirectUri = "http://127.0.0.1:53682/";

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(RedirectUri);

    public bool IsLoopbackRedirect
    {
        get
        {
            if (!Uri.TryCreate(RedirectUri, UriKind.Absolute, out var url)) return false;
            var scheme = url.Scheme.ToLowerInvariant();
            if (scheme is not ("http" or "https")) return false;
            var host = url.Host.ToLowerInvariant();
            return host is "127.0.0.1" or "localhost";
        }
    }

    public int LoopbackPort
    {
        get
        {
            if (Uri.TryCreate(RedirectUri, UriKind.Absolute, out var url) && url.Port > 0)
                return url.Port;
            return 53682;
        }
    }

    /// <summary>
    /// Resolves config from environment variables (and optional overrides). Returns null when client_id is missing.
    /// </summary>
    public static GoogleOAuthConfig? FromEnvironment(
        IReadOnlyDictionary<string, string>? environment = null,
        string? clientIdOverride = null,
        string? clientSecretOverride = null,
        string? redirectOverride = null)
    {
        var env = environment ?? CaptureEnvironment();
        var clientId = FirstNonEmpty(clientIdOverride, Get(env, "MUSES_GOOGLE_OAUTH_CLIENT_ID"));
        var clientSecret = FirstNonEmpty(clientSecretOverride, Get(env, "MUSES_GOOGLE_OAUTH_CLIENT_SECRET")) ?? "";
        var redirect = FirstNonEmpty(redirectOverride, Get(env, "MUSES_GOOGLE_OAUTH_REDIRECT_URI"))
                       ?? DefaultRedirectUri;
        if (string.IsNullOrWhiteSpace(clientId)) return null;
        var config = new GoogleOAuthConfig(clientId.Trim(), clientSecret, redirect.Trim(), DefaultScopes);
        return config.IsValid && config.IsLoopbackRedirect ? config : null;
    }

    private static Dictionary<string, string> CaptureEnvironment()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (global::System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string k && entry.Value is string v)
                dict[k] = v;
        }
        return dict;
    }

    private static string? Get(IReadOnlyDictionary<string, string> env, string key) =>
        env.TryGetValue(key, out var v) ? v : null;

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v)) return v;
        }
        return null;
    }
}
