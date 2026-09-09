using Muses.Core.Platform;

namespace Muses.Infrastructure.Platform;

/// <summary>In-memory credential locker for tests and non-Windows fallback when OS locker is unavailable.</summary>
public sealed class MemoryCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, byte[]> _store = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public byte[]? Get(string account)
    {
        lock (_gate)
            return _store.TryGetValue(account, out var data) ? data.ToArray() : null;
    }

    public bool Set(string account, byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        lock (_gate)
        {
            _store[account] = data.ToArray();
            return true;
        }
    }

    public bool Delete(string account)
    {
        lock (_gate)
        {
            _store.Remove(account);
            return true;
        }
    }
}
