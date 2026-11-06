using Muses.Core.L10n;

namespace Muses.App.ViewModels;

public sealed class PodcastLabels
{
    public static string Title => L10n.Tr("Podcasts", "播客", "Podcast");
    public string FollowedTitle => L10n.Tr("Followed Podcasts", "已关注的播客", "已追蹤的 Podcast");
    public string Search => L10n.Tr("Search", "搜索", "搜尋");
    public string ClearSearch => L10n.Tr("Clear search", "清除搜索", "清除搜尋");
    public string SearchPrompt => L10n.Tr("Search YouTube Music podcasts", "搜索 YouTube Music 播客", "搜尋 YouTube Music Podcast");
    public string SearchResults => L10n.Tr("Podcasts on YouTube Music", "YouTube Music 上的播客", "YouTube Music 上的 Podcast");
    public string Searching => L10n.Tr("Searching YouTube Music…", "正在搜索 YouTube Music…", "正在搜尋 YouTube Music…");
    public string NoSearchResults => L10n.Tr("No podcasts found.", "未找到播客。", "找不到 Podcast。");
    public string SearchFailed => L10n.Tr("YouTube Music could not load these results.", "无法加载这些 YouTube Music 结果。", "無法載入這些 YouTube Music 結果。");
    public string EmptyTitle => L10n.Tr("No followed podcasts", "尚未关注播客", "尚未追蹤 Podcast");
    public string EmptyHint => L10n.Tr("Search for a show and follow it to find its episodes here.", "搜索并关注一个节目，即可在此查看单集。", "搜尋並追蹤節目，即可在這裡查看單集。");
    public string ContinueTitle => L10n.Tr("Continue listening", "继续收听", "繼續收聽");
    public string ContinueHint => L10n.Tr("Podcasts · saved progress", "播客 · 已保存进度", "Podcast · 已儲存進度");
    public string Back => L10n.Tr("Back to Podcasts", "返回播客", "返回 Podcast");
    public string Refresh => L10n.Tr("Refresh Podcast", "刷新播客", "重新整理 Podcast");
    public string Retry => L10n.Tr("Retry", "重试", "重試");
    public string LoadingEpisodes => L10n.Tr("Loading episodes…", "正在加载单集…", "正在載入單集…");
    public string Episodes => L10n.Tr("Episodes", "单集", "單集");
    public string NoEpisodes => L10n.Tr("No episodes available.", "暂无可用单集。", "暫無可用單集。");
    public string LoadMore => L10n.Tr("Load more", "加载更多", "載入更多");
    public string ShowLoadFailed => L10n.Tr("Episodes could not load.", "无法加载单集。", "無法載入單集。");
    public string SaveFailed => L10n.Tr("Podcast changes could not be saved. Please retry.", "播客更改未能保存，请重试。", "Podcast 更改未能儲存，請重試。");
    public string PlayFailed => L10n.Tr("Playback could not start. Please try again.", "无法开始播放，请重试。", "無法開始播放，請再試一次。");
    public string PlayUnplayed => L10n.Tr("Play Unplayed", "播放未听单集", "播放未聽單集");

    public static string FollowAction(bool isFollowing) => isFollowing
        ? L10n.Tr("Unfollow", "取消关注", "取消追蹤")
        : L10n.Tr("Follow", "关注", "追蹤");

    public static string UnplayedOrder(bool byPublicationDate) => byPublicationDate
        ? L10n.Tr("Oldest first by publication date", "按发布日期从旧到新", "依發布日期由舊到新")
        : L10n.Tr("Source order", "来源顺序", "來源順序");

    public static string Played => L10n.Tr("Played", "已听完", "已聽完");
    public static string Unplayed => L10n.Tr("Unplayed", "未听", "未聽");
    public static string Unavailable => L10n.Tr("Unavailable", "不可用", "無法使用");

    public static string ResumeAt(string position) => L10n.Tr(
        $"Resume at {position}", $"从 {position} 继续", $"從 {position} 繼續");

    public static string PlayEpisode(string title, bool canResume) => canResume
        ? L10n.Tr($"Resume {title}", $"继续收听 {title}", $"繼續收聽 {title}")
        : L10n.Tr($"Play {title}", $"播放 {title}", $"播放 {title}");

    public static string MarkEpisode(string title, bool isCompleted) => isCompleted
        ? L10n.Tr($"Mark {title} unplayed", $"将 {title} 标为未听", $"將 {title} 標為未聽")
        : L10n.Tr($"Mark {title} played", $"将 {title} 标为已听", $"將 {title} 標為已聽");
}
