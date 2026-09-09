namespace Muses.Core.Preferences;

public interface IPreferences
{
    double GetDouble(string key, double fallback);
    void SetDouble(string key, double value);
    bool GetBool(string key, bool fallback);
    void SetBool(string key, bool value);
    string GetString(string key, string fallback);
    void SetString(string key, string value);
}

public sealed class MemoryPreferences : IPreferences
{
    public static MemoryPreferences Empty { get; } = new();
    private readonly Dictionary<string, double> _doubles = new();
    private readonly Dictionary<string, bool> _bools = new();
    private readonly Dictionary<string, string> _strings = new();

    public double GetDouble(string key, double fallback) => _doubles.TryGetValue(key, out var v) ? v : fallback;
    public void SetDouble(string key, double value) => _doubles[key] = value;
    public bool GetBool(string key, bool fallback) => _bools.TryGetValue(key, out var v) ? v : fallback;
    public void SetBool(string key, bool value) => _bools[key] = value;
    public string GetString(string key, string fallback) => _strings.TryGetValue(key, out var v) ? v : fallback;
    public void SetString(string key, string value) => _strings[key] = value;
}
