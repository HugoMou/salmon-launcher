using SL.Core.Boot;

namespace SL.Core.Settings;

/// <summary>
/// 配置服务:包装 Data 层的原子 JSON 存储。
/// UI 或其他模块修改配置时统一经过此处,保证落盘与变更通知一致。
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private readonly Data.Settings.AppSettingsStore _store;
    private AppConfig _current;

    public event Action<AppConfig>? Changed;

    public SettingsService()
    {
        Boot.Runtime.EnsureBaseDirectories();
        _store = new Data.Settings.AppSettingsStore(Boot.Runtime.SettingsFile);
        _current = Map(_store.Load());
    }

    public AppConfig Current => _current;

    public void Update(Action<AppConfig> mutate)
    {
        AppConfig before = Clone(_current);
        mutate(_current);

        // 原子落盘(失败时不抛出致命异常,仅记录日志:配置丢失不应打断用户操作)
        try
        {
            _store.Save(Map(_current));
        }
        catch (Exception ex)
        {
            Logging.AppLog.Error("Settings", ex);
        }

        if (!ConfigEquals(before, _current))
            Changed?.Invoke(_current);
    }

    private static AppConfig Map(Data.Settings.AppSettings s) => new()
    {
        Theme = s.ThemeMode switch
        {
            "light" => ThemePref.Light,
            "system" => ThemePref.FollowSystem,
            _ => ThemePref.Dark,
        },
        TransferConcurrency = Math.Clamp(s.TransferConcurrency, 2, 16),
        AccountName = string.IsNullOrWhiteSpace(s.AccountName) ? "Rio" : s.AccountName,
        AccountType = s.AccountType == "msa" ? "msa" : "offline",
        Palette = string.IsNullOrWhiteSpace(s.ThemePalette) ? "salmon" : s.ThemePalette,
        Accent = string.IsNullOrWhiteSpace(s.AccentColor) ? "coral" : s.AccentColor,
        MemoryMb = Math.Clamp(s.MemoryMb, 512, 32768),
        JvmExtraArgs = s.JvmExtraArgs ?? "",
        GameWidth = Math.Clamp(s.GameWidth, 320, 7680),
        GameHeight = Math.Clamp(s.GameHeight, 240, 4320),
        DownloadSource = s.DownloadSource == "bmclapi" ? "bmclapi" : "auto",
        ModrinthMirror = s.ModrinthMirror,
        TermsAccepted = s.TermsAccepted,
        Language = string.IsNullOrWhiteSpace(s.Language) ? "auto" : s.Language,
        OnboardingCompleted = s.OnboardingCompleted,
    };

    private static Data.Settings.AppSettings Map(AppConfig c) => new()
    {
        ThemeMode = c.Theme switch
        {
            ThemePref.Light => "light",
            ThemePref.FollowSystem => "system",
            _ => "dark",
        },
        TransferConcurrency = Math.Clamp(c.TransferConcurrency, 2, 16),
        AccountName = c.AccountName,
        AccountType = c.AccountType,
        ThemePalette = c.Palette,
        AccentColor = c.Accent,
        MemoryMb = Math.Clamp(c.MemoryMb, 512, 32768),
        JvmExtraArgs = c.JvmExtraArgs,
        GameWidth = Math.Clamp(c.GameWidth, 320, 7680),
        GameHeight = Math.Clamp(c.GameHeight, 240, 4320),
        DownloadSource = c.DownloadSource,
        ModrinthMirror = c.ModrinthMirror,
        TermsAccepted = c.TermsAccepted,
        Language = c.Language,
        OnboardingCompleted = c.OnboardingCompleted,
    };

    private static AppConfig Clone(AppConfig c) => new()
    {
        Theme = c.Theme,
        TransferConcurrency = c.TransferConcurrency,
        AccountName = c.AccountName,
        AccountType = c.AccountType,
        Palette = c.Palette,
        Accent = c.Accent,
        ActiveAccountId = c.ActiveAccountId,
        MemoryMb = c.MemoryMb,
        JvmExtraArgs = c.JvmExtraArgs,
        GameWidth = c.GameWidth,
        GameHeight = c.GameHeight,
        DownloadSource = c.DownloadSource,
        ModrinthMirror = c.ModrinthMirror,
        TermsAccepted = c.TermsAccepted,
        Language = c.Language,
    };

    private static bool ConfigEquals(AppConfig a, AppConfig b)
        => a.Theme == b.Theme
           && a.TransferConcurrency == b.TransferConcurrency
           && a.AccountName == b.AccountName
           && a.AccountType == b.AccountType
           && a.Palette == b.Palette
           && a.Accent == b.Accent
           && a.MemoryMb == b.MemoryMb
           && a.JvmExtraArgs == b.JvmExtraArgs
           && a.GameWidth == b.GameWidth
           && a.GameHeight == b.GameHeight
           && a.DownloadSource == b.DownloadSource
           && a.ModrinthMirror == b.ModrinthMirror
           && a.TermsAccepted == b.TermsAccepted
           && a.Language == b.Language
           && a.OnboardingCompleted == b.OnboardingCompleted;
}
