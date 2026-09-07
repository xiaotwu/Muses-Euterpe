using Muses.Core.L10n;

namespace Muses.Tests;

public class L10nTests : IDisposable
{
    private readonly ILanguageSource _previous = L10n.Source;

    public void Dispose() => L10n.Source = _previous;

    [Fact]
    public void English_preference_returns_english()
    {
        L10n.Source = new FixedLanguageSource("en");
        Assert.Equal("Home", L10n.Tr("Home", "首页"));
        Assert.False(L10n.IsChinese);
    }

    [Fact]
    public void Zh_preference_returns_simplified_chinese()
    {
        L10n.Source = new FixedLanguageSource("zh");
        Assert.Equal("首页", L10n.Tr("Home", "首页"));
        Assert.True(L10n.IsChinese);
    }

    [Fact]
    public void Traditional_falls_back_to_simplified_then_english()
    {
        L10n.Source = new FixedLanguageSource("zh-Hant");
        Assert.Equal("首頁", L10n.Tr("Home", "首页", zhHant: "首頁"));
        Assert.Equal("首页", L10n.Tr("Home", "首页"));
    }

    [Fact]
    public void Japanese_falls_back_to_english()
    {
        L10n.Source = new FixedLanguageSource("ja");
        Assert.Equal("Home", L10n.Tr("Home", "首页"));
        Assert.Equal("ホーム", L10n.Tr("Home", "首页", ja: "ホーム"));
    }

    [Fact]
    public void System_chinese_hans_is_detected()
    {
        L10n.Source = new FixedLanguageSource("system", "zh", "Hans");
        Assert.True(L10n.IsChinese);
        Assert.Equal("首页", L10n.Tr("Home", "首页"));
    }
}
