namespace Muses.Core.Account;

public enum YouTubeAccountState
{
    SignedOut,
    SigningIn,
    SignedIn,
    Error
}

public sealed record YouTubeUserProfile(
    string ChannelId,
    string DisplayName,
    string? AvatarUrl,
    string? Handle
);

public sealed record YouTubeAuthTokens(
    string AccessToken,
    string? RefreshToken,
    DateTimeOffset ExpiresAt
);
