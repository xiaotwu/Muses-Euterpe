namespace Muses.Core.L10n;

/// <summary>
/// Runtime bilingual strings. English is the default; Simplified Chinese is the extra locale.
/// Traditional Chinese and Japanese fall back to Hans then English.
/// </summary>
public static class L10n
{
    public static ILanguageSource Source { get; set; } = SystemLanguageSource.Instance;

    public static bool IsChinese
    {
        get
        {
            var pref = Source.Preference;
            if (pref == "system")
                return Source.SystemLanguageCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            return pref is "zh" or "zh-Hans" or "zh-Hant";
        }
    }

    public static string Tr(string en, string zhHans, string? zhHant = null, string? ja = null)
    {
        var code = ResolveCode();
        return code switch
        {
            "zh" or "zh-Hans" => zhHans,
            "zh-Hant" => zhHant ?? zhHans,
            "ja" => ja ?? en,
            _ => en
        };
    }

    private static string ResolveCode()
    {
        var pref = Source.Preference;
        if (pref != "system") return pref;
        var id = Source.SystemLanguageCode;
        if (id == "zh")
            return Source.SystemScript == "Hant" ? "zh-Hant" : "zh-Hans";
        return id;
    }
}

public interface ILanguageSource
{
    /// <summary>system | en | zh | zh-Hans | zh-Hant</summary>
    string Preference { get; }
    string SystemLanguageCode { get; }
    string? SystemScript { get; }
}

public sealed class SystemLanguageSource : ILanguageSource
{
    public static readonly SystemLanguageSource Instance = new();

    public string Preference { get; set; } = "system";
    public string SystemLanguageCode { get; set; } = "en";
    public string? SystemScript { get; set; }
}

public sealed class FixedLanguageSource : ILanguageSource
{
    public FixedLanguageSource(string preference, string systemLanguageCode = "en", string? systemScript = null)
    {
        Preference = preference;
        SystemLanguageCode = systemLanguageCode;
        SystemScript = systemScript;
    }

    public string Preference { get; }
    public string SystemLanguageCode { get; }
    public string? SystemScript { get; }
}
