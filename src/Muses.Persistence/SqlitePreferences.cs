using Muses.Core.Preferences;

namespace Muses.Persistence;

/// <summary>SQLite-backed IPreferences. Survives app restart.</summary>
public sealed class SqlitePreferences : IPreferences
{
    private readonly IPreferencesStore _store;

    public SqlitePreferences(IPreferencesStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public double GetDouble(string key, double fallback) => _store.GetPreferenceDouble(key, fallback);
    public void SetDouble(string key, double value) => _store.SetPreferenceDouble(key, value);
    public bool GetBool(string key, bool fallback) => _store.GetPreferenceBool(key, fallback);
    public void SetBool(string key, bool value) => _store.SetPreferenceBool(key, value);
    public string GetString(string key, string fallback) => _store.GetPreferenceString(key, fallback);
    public void SetString(string key, string value) => _store.SetPreferenceString(key, value);
}
