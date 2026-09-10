using CommunityToolkit.Mvvm.Input;
using Muses.Core.L10n;
using Muses.Core.Preferences;
using Muses.Infrastructure;
using Muses.Infrastructure.Playback;

namespace Muses.App.ViewModels;

/// <summary>
/// Settings / prefs chrome for <see cref="ShellViewModel"/>. Kept as a partial so
/// playback and overlay stay in the main file.
/// </summary>
public partial class ShellViewModel
{
    public bool ReplayGainEnabled
    {
        get => Preferences.GetBool(PrefKey.ReplayGainEnabled, true);
        set
        {
            if (ReplayGainEnabled == value) return;
            Preferences.SetBool(PrefKey.ReplayGainEnabled, value);
            _streamEngine?.SetReplayGainEnabled(value);
            OnPropertyChanged(nameof(ReplayGainEnabled));
        }
    }

    public bool CloseToTray
    {
        get => Preferences.GetBool(PrefKey.CloseToTray, true);
        set
        {
            if (CloseToTray == value) return;
            Preferences.SetBool(PrefKey.CloseToTray, value);
            OnPropertyChanged(nameof(CloseToTray));
        }
    }

    public bool TrayEnabled
    {
        get => FeatureFlagDefaults.IsEnabled(Preferences, PrefKey.FfTray);
        set
        {
            if (TrayEnabled == value) return;
            Preferences.SetBool(PrefKey.FfTray, value);
            Tray?.SetEnabled(value);
            OnPropertyChanged(nameof(TrayEnabled));
        }
    }

    public bool MiniPlayerEnabled
    {
        get => FeatureFlagDefaults.IsEnabled(Preferences, PrefKey.FfMiniPlayer);
        set
        {
            if (MiniPlayerEnabled == value) return;
            Preferences.SetBool(PrefKey.FfMiniPlayer, value);
            OnPropertyChanged(nameof(MiniPlayerEnabled));
        }
    }

    public bool DesktopLyricsEnabled
    {
        get => FeatureFlagDefaults.IsEnabled(Preferences, PrefKey.FfDesktopLyrics);
        set
        {
            if (DesktopLyricsEnabled == value) return;
            Preferences.SetBool(PrefKey.FfDesktopLyrics, value);
            OnPropertyChanged(nameof(DesktopLyricsEnabled));
        }
    }

    /// <summary>
    /// Optional system-wide hotkeys (default off). In-window Ctrl+P / Ctrl+Left / Ctrl+Right
    /// are always handled by MainWindow when focused — this flag does not gate those.
    /// </summary>
    public bool GlobalHotkeysEnabled
    {
        get => FeatureFlagDefaults.IsEnabled(Preferences, PrefKey.FfGlobalHotkeys);
        set
        {
            if (GlobalHotkeysEnabled == value) return;
            Preferences.SetBool(PrefKey.FfGlobalHotkeys, value);
            _globalHotkeys?.SetEnabled(value);
            OnPropertyChanged(nameof(GlobalHotkeysEnabled));
        }
    }

    public string LanguagePreference
    {
        get => Preferences.GetString(PrefKey.Language, "system");
        set
        {
            if (LanguagePreference == value) return;
            Preferences.SetString(PrefKey.Language, value);
            if (L10n.Source is SystemLanguageSource sys)
                sys.Preference = value;
            OnPropertyChanged(nameof(LanguagePreference));
            NotifyL10nLabels();
        }
    }

    public int LanguageSelectedIndex
    {
        get => LanguagePreference switch
        {
            "en" => 1,
            "zh" or "zh-Hans" => 2,
            _ => 0
        };
        set
        {
            LanguagePreference = value switch
            {
                1 => "en",
                2 => "zh",
                _ => "system"
            };
            OnPropertyChanged(nameof(LanguageSelectedIndex));
        }
    }

    public string ThemePreference
    {
        get => Preferences.GetString(PrefKey.Theme, "dark");
        set
        {
            if (ThemePreference == value) return;
            Preferences.SetString(PrefKey.Theme, value);
            global::Muses.App.App.ApplyTheme(Preferences);
            OnPropertyChanged(nameof(ThemePreference));
            OnPropertyChanged(nameof(ThemeSelectedIndex));
        }
    }

    public int ThemeSelectedIndex
    {
        get => ThemePreference switch
        {
            "light" => 1,
            "system" => 2,
            _ => 0
        };
        set
        {
            ThemePreference = value switch
            {
                1 => "light",
                2 => "system",
                _ => "dark"
            };
            OnPropertyChanged(nameof(ThemeSelectedIndex));
        }
    }

    public double CrossfadeSeconds
    {
        get => Preferences.GetDouble(PrefKey.CrossfadeSeconds, 0);
        set
        {
            var clamped = Math.Clamp(value, 0, 12);
            if (Math.Abs(CrossfadeSeconds - clamped) < 0.01) return;
            Preferences.SetDouble(PrefKey.CrossfadeSeconds, clamped);
            _streamEngine?.SetCrossfadeSeconds(clamped);
            Playback?.SetCrossfadeSeconds(clamped);
            OnPropertyChanged(nameof(CrossfadeSeconds));
            OnPropertyChanged(nameof(CrossfadeLabel));
        }
    }

    public string CrossfadeLabel => CrossfadeSeconds < 0.05
        ? L10n.Tr("Off (YouTube streams are not gapless)", "关（YouTube 流无法无缝）")
        : L10n.Tr($"{CrossfadeSeconds:0.#} s overlap", $"{CrossfadeSeconds:0.#} 秒重叠");

    public bool IsWebHomeAvailable => WebHome?.Status == Muses.Core.Advanced.WebHomeSessionStatus.Available;
    public string WebHomeVerifiedLabel => L10n.Tr(
        "Web Home session matches this account. Personalized Home stays off until a lawful feed is wired.",
        "Web Home 会话与此账号匹配。在接入合法信息流之前，个性化首页不会开启。");

    public string SettingsThemeLightLabel => L10n.Tr("Light", "浅色");
    public string SettingsCrossfadeLabel => L10n.Tr("Crossfade", "交叉淡化");
    public string SettingsCrossfadeBody => L10n.Tr(
        "Overlaps the current and next mpv session. 0 is off. YouTube URLs are not gapless; mpv uses weak gapless when consecutive codecs match.",
        "重叠当前与下一首 mpv 会话。0 为关闭。YouTube 流无法无缝；编码相同时 mpv 使用弱无缝。");
    public string SettingsClearCacheLabel => L10n.Tr("Clear Cache", "清除缓存");
    public string SettingsClearCacheBody => L10n.Tr(
        "Deletes artwork and Home feed caches. Playlists and history stay.",
        "删除封面与首页缓存。歌单与历史保留。");
    public string SettingsResetDataLabel => L10n.Tr("Reset Data", "重置数据");
    public string SettingsResetDataBody => L10n.Tr(
        "Deletes the local library database and caches, signs out, then quits Muses. Tokens are removed from Credential Manager. This cannot be undone.",
        "删除本地曲库数据库与缓存，退出登录并关闭 Muses。凭证从凭据管理器删除。此操作不可撤销。");
    public string SettingsResetDataConfirmLabel => ResetDataArmed
        ? L10n.Tr("Click again to erase and quit", "再点一次将清除并退出")
        : L10n.Tr("Reset Data…", "重置数据…");
    public string SettingsCacheStatus { get; private set; } = "";
    public bool ResetOnExit { get; private set; }
    public bool ResetDataArmed { get; private set; }

    [RelayCommand]
    public void ClearCache()
    {
        var n = LocalDataMaintenance.ClearCache();
        SettingsCacheStatus = L10n.Tr($"Cleared {n} cached files.", $"已清除 {n} 个缓存文件。");
        OnPropertyChanged(nameof(SettingsCacheStatus));
    }

    [RelayCommand]
    public void ResetData()
    {
        if (!ResetDataArmed)
        {
            ResetDataArmed = true;
            SettingsCacheStatus = L10n.Tr("This cannot be undone. Click again to confirm.", "此操作不可撤销。再点一次确认。");
            OnPropertyChanged(nameof(ResetDataArmed));
            OnPropertyChanged(nameof(SettingsResetDataConfirmLabel));
            OnPropertyChanged(nameof(SettingsCacheStatus));
            return;
        }

        try
        {
            ResetOnExit = true;
            Account?.SignOut();
            RequestAppExit?.Invoke();
        }
        catch
        {
            SettingsCacheStatus = L10n.Tr("Reset failed.", "重置失败。");
            OnPropertyChanged(nameof(SettingsCacheStatus));
        }
    }

    public string AudioQuality
    {
        get => Preferences.GetString(PrefKey.AudioQuality, "best");
        set
        {
            if (AudioQuality == value) return;
            Preferences.SetString(PrefKey.AudioQuality, value);
            _streamEngine?.SetStreamQuality(ProcessStreamEngine.MapAudioQualityPref(value));
            OnPropertyChanged(nameof(AudioQuality));
            OnPropertyChanged(nameof(AudioQualitySelectedIndex));
        }
    }

    public int AudioQualitySelectedIndex
    {
        get => AudioQuality switch
        {
            "high" => 1,
            "medium" => 2,
            _ => 0
        };
        set
        {
            AudioQuality = value switch
            {
                1 => "high",
                2 => "medium",
                _ => "best"
            };
            OnPropertyChanged(nameof(AudioQualitySelectedIndex));
        }
    }

    public bool ResumeAfterVideo
    {
        get => Preferences.GetBool(PrefKey.ResumeAfterVideo, true);
        set
        {
            if (ResumeAfterVideo == value) return;
            Preferences.SetBool(PrefKey.ResumeAfterVideo, value);
            OnPropertyChanged(nameof(ResumeAfterVideo));
        }
    }

    public string SettingsGeneralTitle => L10n.Tr("General Settings", "通用设置");
    public string SettingsLanguageLabel => L10n.Tr("Language", "语言");
    public string SettingsCloseToTrayLabel => L10n.Tr("Close button minimizes to System Tray", "关闭按钮最小化到系统托盘");
    public string SettingsTrayLabel => L10n.Tr("Show tray / notification area icon", "显示托盘 / 通知区域图标");
    public string SettingsPlaybackTitle => L10n.Tr("Playback Settings", "播放设置");
    public string SettingsResumeAfterVideoLabel => L10n.Tr("Resume music playback when video overlay closes", "关闭视频浮层后恢复音乐播放");
    public string SettingsReplayGainLabel => L10n.Tr("Volume normalization (ReplayGain)", "音量标准化（ReplayGain）");
    public string SettingsYouTubeTitle => L10n.Tr("YouTube Account", "YouTube 账号");
    public string SettingsGoogleSignInLabel => L10n.Tr("Google Account Sign-In", "Google 账号登录");
    public string SettingsGoogleSignInBody => L10n.Tr(
        "Sign in to personalize Home and sync your liked tracks and playlists.",
        "登录后即可个性化首页并同步喜欢的歌曲与歌单。");
    public string SettingsAudioQualityTitle => L10n.Tr("Audio Quality", "音频质量");
    public string SettingsPreferredStreamLabel => L10n.Tr("Preferred Stream Quality", "首选流质量");
    public string SettingsAppearanceTitle => L10n.Tr("Appearance", "外观");
    public string SettingsThemeLabel => L10n.Tr("Theme", "主题");
    public string SettingsAccentLabel => L10n.Tr("Accent Color", "强调色");
    public string SettingsLyricsTitle => L10n.Tr("Lyrics Settings", "歌词设置");
    public string SettingsLyricsProviderNote => L10n.Tr(
        "Lyrics are fetched from LRCLIB (synced and plain). No alternate provider is wired yet.",
        "歌词来自 LRCLIB（支持逐行与纯文本）。尚未接入其他提供方。");
    public string SettingsDesktopTitle => L10n.Tr("Desktop Tools & Widgets", "桌面工具与小组件");
    public string SettingsMiniPlayerLabel => L10n.Tr("Mini Player", "迷你播放器");
    public string SettingsMiniPlayerBody => L10n.Tr(
        "Compact floating player window with always-on-top mode.",
        "可置顶的紧凑浮动播放器窗口。");
    public string SettingsDesktopLyricsLabel => L10n.Tr("Desktop Lyrics", "桌面歌词");
    public string SettingsDesktopLyricsBody => L10n.Tr(
        "Transparent, draggable desktop lyrics banner on top of other windows.",
        "可拖动的透明桌面歌词条，置于其他窗口之上。");
    public string SettingsGlobalHotkeysLabel => L10n.Tr(
        "System-wide transport hotkeys (experimental, default off)",
        "系统级播放快捷键（实验性，默认关闭）");
    public string SettingsGlobalHotkeysBody => L10n.Tr(
        "When on, Ctrl+P / Ctrl+Left / Ctrl+Right register system-wide (they steal those chords from other apps). In-window chords always work while Muses is focused, even if this is off.",
        "开启后 Ctrl+P / Ctrl+Left / Ctrl+Right 会全局注册（会占用其他应用的这些快捷键）。即使关闭，Muses 聚焦时窗口内快捷键始终可用。");
    public string SettingsWebHomeTitle => L10n.Tr("Web Home (opt-in)", "Web Home（需同意）");
    public string SettingsWebHomeBody => L10n.Tr(
        "Uses an isolated helper process with an ephemeral cookie jar deleted on exit. Off by default; requires consent. No scraping runs in the Muses UI process.",
        "使用隔离的辅助进程与退出即删的临时 Cookie。默认关闭，需同意。Muses UI 进程内不进行抓取。");
    public string SettingsCatGeneral => L10n.Tr("General", "通用");
    public string SettingsCatPlayback => L10n.Tr("Playback", "播放");
    public string SettingsCatQuality => L10n.Tr("Audio Quality", "音频质量");
    public string SettingsCatAppearance => L10n.Tr("Appearance", "外观");
    public string SettingsCatYouTube => L10n.Tr("YouTube", "YouTube");
    public string SettingsCatLyrics => L10n.Tr("Lyrics", "歌词");
    public string SettingsCatDesktop => L10n.Tr("Desktop", "桌面");
    public string SettingsCatAbout => L10n.Tr("About", "关于");
    public string SettingsEngineStatusTitle => L10n.Tr("Stream engine", "流引擎");
    public bool IsMpvAvailable => MpvPlayerFactory.LocateMpvBinary() is not null;
    public string SettingsEngineStatusBody => IsMpvAvailable
        ? L10n.Tr(
            "mpv found on PATH or under resources/. yt-dlp resolves stream URLs; mpv emits audio over IPC.",
            "已在 PATH 或 resources/ 找到 mpv。yt-dlp 解析流地址，mpv 通过 IPC 输出音频。")
        : L10n.Tr(
            "mpv was not found. Place mpv.exe under resources/ or on PATH. This page will not claim the engine is operational until mpv is present.",
            "未找到 mpv。请将 mpv.exe 放到 resources/ 或 PATH。在找到 mpv 之前，本页不会声称引擎可用。");
    public string SettingsReplayGainUnavailableNote => L10n.Tr(
        "When enabled, track ReplayGain (dB) is applied via mpv volume adjustment when metadata is present.",
        "开启后，若曲目含 ReplayGain（dB）元数据，将通过 mpv 音量调整应用。");
    public string SettingsQualityBestLabel => L10n.Tr("Best (Opus / AAC)", "最佳（Opus / AAC）");
    public string SettingsQualityHighLabel => L10n.Tr("High (128kbps)", "高（128kbps）");
    public string SettingsQualityMediumLabel => L10n.Tr("Medium (64kbps)", "中（64kbps）");
    public string SettingsThemeDarkLabel => L10n.Tr("Dark (Muses Default)", "深色（Muses 默认）");
    public string SettingsThemeSystemLabel => L10n.Tr("Follow System", "跟随系统");
    public string SettingsAccentNameLabel => L10n.Tr("Muses Crimson (#FA586A)", "Muses 绯红 (#FA586A)");
    public string SettingsReduceMotionNote => L10n.Tr(
        "Reduce motion follows the Windows accessibility setting. There is no separate in-app override.",
        "减少动态效果跟随 Windows 辅助功能设置，应用内无单独开关。");
    public string SettingsLyricsAutoScrollNote => L10n.Tr(
        "Synced lyrics auto-scroll with the playhead. That behavior is always on.",
        "逐行歌词随播放进度自动滚动，该行为始终开启。");
    public string SettingsToggleWebHomeLabel => L10n.Tr("Toggle Web Home", "开关 Web Home");
    public string SettingsProbeWebHomeLabel => L10n.Tr("Probe Session", "探测会话");
    public string SettingsOpenMiniPlayerLabel => L10n.Tr("Open Mini Player", "打开迷你播放器");
    public string SettingsToggleDesktopLyricsLabel => L10n.Tr("Toggle Desktop Lyrics", "开关桌面歌词");
    public string SettingsInWindowShortcutsTitle => L10n.Tr("In-window shortcuts", "窗口内快捷键");
    public string SettingsShortcutSearch => L10n.Tr("• Ctrl + F : Open Search Window", "• Ctrl + F : 打开搜索窗口");
    public string SettingsShortcutPlay => L10n.Tr("• Ctrl + P / Space : Play / Pause (when Muses is focused)", "• Ctrl + P / 空格 : 播放 / 暂停（Muses 聚焦时）");
    public string SettingsShortcutNext => L10n.Tr("• Ctrl + Right : Next Track", "• Ctrl + Right : 下一首");
    public string SettingsShortcutPrev => L10n.Tr("• Ctrl + Left : Previous Track", "• Ctrl + Left : 上一首");
    public string SettingsAboutTagline => L10n.Tr(
        "A native desktop YouTube-native music application. Display name Muses, repo/solution Euterpe. Built with C# / .NET 10 and Avalonia.",
        "原生桌面 YouTube 音乐应用。显示名 Muses，仓库/解决方案 Euterpe。使用 C# / .NET 10 与 Avalonia 构建。");
    public string SettingsAboutVersion => L10n.Tr("Version 0.1.0 (Windows 11)", "版本 0.1.0（Windows 11）");
    public string SettingsAboutAuthor => L10n.Tr("Author: xiaotwu", "作者：xiaotwu");
    public string SettingsSoftwareUpdateTitle => L10n.Tr("Software Update", "软件更新");
    public string SettingsSoftwareUpdateBody => L10n.Tr(
        "Check for newer versions of Muses on GitHub.",
        "在 GitHub 上检查 Muses 的新版本。");
    public string SettingsCheckForUpdatesLabel => L10n.Tr("Check for Updates", "检查更新");
    public string SettingsDownloadUpdateLabel => L10n.Tr("Download Update", "下载更新");
    public string SettingsViewOnGitHubLabel => L10n.Tr("View on GitHub", "在 GitHub 上查看");
    public string SettingsAutomationNote => L10n.Tr(
        "Automation is a background engine. There is no visual rules editor yet.",
        "自动化是后台引擎，目前没有可视化规则编辑器。");
    public string SettingsTokensPrivacyLabel => L10n.Tr(
        "Tokens stay in the OS credential locker on this device. No telemetry.",
        "令牌保存在本机操作系统凭证保管库中。无遥测。");

    private void NotifySettingsLabels()
    {
        OnPropertyChanged(nameof(SettingsGeneralTitle));
        OnPropertyChanged(nameof(SettingsLanguageLabel));
        OnPropertyChanged(nameof(SettingsCloseToTrayLabel));
        OnPropertyChanged(nameof(SettingsTrayLabel));
        OnPropertyChanged(nameof(SettingsPlaybackTitle));
        OnPropertyChanged(nameof(SettingsResumeAfterVideoLabel));
        OnPropertyChanged(nameof(SettingsReplayGainLabel));
        OnPropertyChanged(nameof(SettingsYouTubeTitle));
        OnPropertyChanged(nameof(SettingsAudioQualityTitle));
        OnPropertyChanged(nameof(SettingsPreferredStreamLabel));
        OnPropertyChanged(nameof(SettingsAppearanceTitle));
        OnPropertyChanged(nameof(SettingsLyricsTitle));
        OnPropertyChanged(nameof(SettingsDesktopTitle));
        OnPropertyChanged(nameof(SettingsMiniPlayerLabel));
        OnPropertyChanged(nameof(SettingsDesktopLyricsLabel));
        OnPropertyChanged(nameof(SettingsGlobalHotkeysLabel));
        OnPropertyChanged(nameof(SettingsCatGeneral));
        OnPropertyChanged(nameof(SettingsCatPlayback));
        OnPropertyChanged(nameof(SettingsCatQuality));
        OnPropertyChanged(nameof(SettingsCatAppearance));
        OnPropertyChanged(nameof(SettingsCatYouTube));
        OnPropertyChanged(nameof(SettingsCatLyrics));
        OnPropertyChanged(nameof(SettingsCatDesktop));
        OnPropertyChanged(nameof(SettingsCatAbout));
        OnPropertyChanged(nameof(SettingsEngineStatusTitle));
        OnPropertyChanged(nameof(SettingsEngineStatusBody));
        OnPropertyChanged(nameof(SettingsReplayGainUnavailableNote));
        OnPropertyChanged(nameof(SettingsQualityBestLabel));
        OnPropertyChanged(nameof(SettingsQualityHighLabel));
        OnPropertyChanged(nameof(SettingsQualityMediumLabel));
        OnPropertyChanged(nameof(SettingsThemeDarkLabel));
        OnPropertyChanged(nameof(SettingsThemeSystemLabel));
        OnPropertyChanged(nameof(SettingsAccentNameLabel));
        OnPropertyChanged(nameof(SettingsReduceMotionNote));
        OnPropertyChanged(nameof(SettingsLyricsProviderNote));
        OnPropertyChanged(nameof(SettingsLyricsAutoScrollNote));
        OnPropertyChanged(nameof(SettingsToggleWebHomeLabel));
        OnPropertyChanged(nameof(SettingsProbeWebHomeLabel));
        OnPropertyChanged(nameof(SettingsOpenMiniPlayerLabel));
        OnPropertyChanged(nameof(SettingsToggleDesktopLyricsLabel));
        OnPropertyChanged(nameof(SettingsInWindowShortcutsTitle));
        OnPropertyChanged(nameof(SettingsShortcutSearch));
        OnPropertyChanged(nameof(SettingsShortcutPlay));
        OnPropertyChanged(nameof(SettingsShortcutNext));
        OnPropertyChanged(nameof(SettingsShortcutPrev));
        OnPropertyChanged(nameof(SettingsGlobalHotkeysBody));
        OnPropertyChanged(nameof(SettingsWebHomeTitle));
        OnPropertyChanged(nameof(SettingsWebHomeBody));
        OnPropertyChanged(nameof(SettingsTokensPrivacyLabel));
        OnPropertyChanged(nameof(SettingsAboutTagline));
        OnPropertyChanged(nameof(SettingsAboutVersion));
        OnPropertyChanged(nameof(SettingsAboutAuthor));
        OnPropertyChanged(nameof(SettingsSoftwareUpdateTitle));
        OnPropertyChanged(nameof(SettingsSoftwareUpdateBody));
        OnPropertyChanged(nameof(SettingsCheckForUpdatesLabel));
        OnPropertyChanged(nameof(SettingsDownloadUpdateLabel));
        OnPropertyChanged(nameof(SettingsViewOnGitHubLabel));
        OnPropertyChanged(nameof(SettingsAutomationNote));
        OnPropertyChanged(nameof(SettingsMiniPlayerBody));
        OnPropertyChanged(nameof(SettingsDesktopLyricsBody));
        OnPropertyChanged(nameof(SettingsGoogleSignInLabel));
        OnPropertyChanged(nameof(SettingsGoogleSignInBody));
        OnPropertyChanged(nameof(SettingsAccentLabel));
        OnPropertyChanged(nameof(SettingsThemeLabel));
        OnPropertyChanged(nameof(SettingsThemeLightLabel));
        OnPropertyChanged(nameof(SettingsCrossfadeLabel));
        OnPropertyChanged(nameof(SettingsCrossfadeBody));
        OnPropertyChanged(nameof(CrossfadeLabel));
        OnPropertyChanged(nameof(SettingsClearCacheLabel));
        OnPropertyChanged(nameof(SettingsClearCacheBody));
        OnPropertyChanged(nameof(SettingsResetDataLabel));
        OnPropertyChanged(nameof(SettingsResetDataBody));
        OnPropertyChanged(nameof(SettingsResetDataConfirmLabel));
        OnPropertyChanged(nameof(WebHomeVerifiedLabel));
    }
}
