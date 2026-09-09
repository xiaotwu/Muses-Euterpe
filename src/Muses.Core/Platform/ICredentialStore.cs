namespace Muses.Core.Platform;

/// <summary>
/// Platform credential locker. Tokens must never be written as plaintext JSON in app data.
/// </summary>
public interface ICredentialStore
{
    byte[]? Get(string account);
    bool Set(string account, byte[] data);
    bool Delete(string account);
}
