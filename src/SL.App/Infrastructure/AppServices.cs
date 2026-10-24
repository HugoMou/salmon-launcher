using SL.App.Dialogs;
using SL.App.Navigation;
using SL.App.Notify;
using SL.App.Tasks;
using SL.App.Themes;

namespace SL.App.Infrastructure;

/// <summary>组合根:服务对象在应用启动时创建,页面仅通过本静态类获取依赖。</summary>
public static class AppServices
{
    public static Core.Settings.ISettingsService Settings { get; private set; } = null!;
    public static Core.Transfers.ITransferService Transfers { get; private set; } = null!;
    public static Core.Imaging.IImageCacheService Images { get; private set; } = null!;
    public static NotifyService Notify { get; private set; } = null!;
    public static DialogService Dialogs { get; private set; } = null!;
    public static NavService Nav { get; private set; } = null!;
    public static TaskCenterVM TaskCenter { get; private set; } = null!;

    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
            return;
        _initialized = true;

        Settings = new Core.Settings.SettingsService();

        Transfers = new Core.Transfers.TransferEngine(new Core.Transfers.TransferOptions
        {
            MaxConcurrency = Settings.Current.TransferConcurrency,
        });
        Images = new Core.Imaging.ImageCacheService();
        Notify = new NotifyService();
        Dialogs = new DialogService();
        TaskCenter = new TaskCenterVM();
        Nav = new NavService();

        // 配置变更联动:引擎并发数 + 主题(配色/强调色/深浅) + Modrinth 镜像
        Core.Net.Modrinth.PreferMirror = Settings.Current.ModrinthMirror;
        Settings.Changed += cfg =>
        {
            Transfers.Options.MaxConcurrency = cfg.TransferConcurrency;
            ThemeManager.Apply(cfg.Theme, cfg.Palette, cfg.Accent);
            Core.Net.Modrinth.PreferMirror = cfg.ModrinthMirror;
        };
    }
}
