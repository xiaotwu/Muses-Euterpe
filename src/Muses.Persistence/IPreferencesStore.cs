namespace Muses.Persistence;

public interface IPreferencesStore
{
    double GetPreferenceDouble(string key, double fallback);
    void SetPreferenceDouble(string key, double value);
    bool GetPreferenceBool(string key, bool fallback);
    void SetPreferenceBool(string key, bool value);
    string GetPreferenceString(string key, string fallback);
    void SetPreferenceString(string key, string value);
}
