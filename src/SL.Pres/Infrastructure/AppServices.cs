using System.Windows;
using SL.Pres.Dialogs;
using SL.Pres.Navigation;
using SL.Pres.Notify;
using SL.Pres.Tasks;

namespace SL.Pres.Infrastructure;

/// <summary>组合根:服务对象在应用启动时创建,页面仅通过本静态类获取依赖。</summary>
public static class AppServices
{
    public static Core.Settings.ISettingsService Settings { get; private set; } = null!;
    public static Core.Transfers.ITransferService Transfers { get; private set; } = null!;
    public static Core.Imaging.IImageCacheService Images { get; private set; } = null!;
    public static NotifyService Notify { get; private set; } = null!;
    public static DialogService Dialogs { get; private set; } = null!;
    public static NavVM Nav { get; private set; } = null!;
    public static TaskCenterVM TaskCenter { get; private set; } = null!;

    public static void Initialize()
    {
        Core.Boot.Runtime.EnsureBaseDirectories();
        Core.Logging.AppLog.Initialize();

        Settings = new Core.Settings.SettingsService();

        var engineOptions = new Core.Transfers.TransferOptions
        {
            MaxConcurrency = Settings.Current.TransferConcurrency,
        };
        Transfers = new Core.Transfers.TransferEngine(engineOptions);
        Images = new Core.Imaging.ImageCacheService();
        Notify = new NotifyService();
        Dialogs = new DialogService();
        TaskCenter = new TaskCenterVM();

        RegisterPages();
        Nav = new NavVM();

        // 阶段二:后台 Java 扫描预热(不阻塞启动)
        JavaEnv.Warm();

        // 配置变更 → 引擎参数联动(例如并发数)
        Settings.Changed += cfg =>
        {
            Transfers.Options.MaxConcurrency = cfg.TransferConcurrency;
        };
    }

    // ------------------------------------------------------------ 页面注册

    /// <summary>按页面 Key 前缀决定所属分区的左侧面板(同一分区共享一份,按 Key 缓存)。</summary>
    private static Func<UIElement> LeftOf(string key) => key switch
    {
        "launch.home" => () => new Panels.LaunchPanelLeft(),
        "launch.tasks" => () => new Panels.LaunchPanelLeft(useTasksHint: true),
        _ when key.StartsWith("inst.", StringComparison.Ordinal) => () => new Panels.InstancePanelLeft(),
        _ when key.StartsWith("dl.", StringComparison.Ordinal) => () => new Panels.DownloadPanelLeft(),
        _ when key.StartsWith("st.", StringComparison.Ordinal) => () => new Panels.SettingsPanelLeft(),
        _ => () => new Panels.ToolsPanelLeft(),
    };

    private static void Add(string key, string title, int level, Func<UIElement> content)
    {
        PageCatalog.Add(new PageSpec
        {
            Key = key,
            Title = title,
            Level = level,
            BuildLeft = LeftOf(key),
            BuildContent = content,
        });
    }

    private static void RegisterPages()
    {
        // —— 启动 ——
        Add("launch.home", "启动面板", 0,
            () => new Pages.LaunchHomeView { DataContext = new LaunchHomeVM() });
        Add("launch.tasks", "任务中心", 1,
            () => new Pages.TaskCenterView { DataContext = TaskCenter });

        // —— 下载 ——
        Add("dl.game", "游戏版本", 0,
            () => new Pages.VersionListView { DataContext = new VersionListVM() });
        Add("dl.loaders", "加载器选择", 1,
            () => new Pages.LoaderListView { DataContext = new LoaderListVM() });
        Add("dl.install", "安装概览", 1,
            () => new Pages.InstallSummaryView { DataContext = new InstallSummaryVM() });
        Add("dl.pack", "模组仓库", 0,
            () => new Pages.ModrinthView { DataContext = new ModrinthVM() });
        Add("dl.shaders", "光影仓库", 0,
            () => new Pages.ModrinthView { DataContext = new ModrinthVM(Core.Net.ModrinthCategory.Shaders) });
        Add("dl.packs", "资源包仓库", 0,
            () => new Pages.ModrinthView { DataContext = new ModrinthVM(Core.Net.ModrinthCategory.ResourcePacks) });

        // —— 设置 ——
        Add("st.launch", "启动设置", 0,
            () => new Pages.SettingsView { DataContext = new SettingsPageVM() });
        Add("st.global", "全局偏好", 0,
            () => new Pages.InstancePlaceholderView { DataContext = new PageSimpleVM("全局偏好",
                "阶段三接入:窗口尺寸、代理、语言与个人目录设置。",
                ["代理与镜像源配置", "界面语言切换", "个人目录位置自定义"]) });
        Add("st.about", "关于", 0,
            () => new Pages.InstancePlaceholderView { DataContext = new PageSimpleVM("关于",
                "Salmon Launcher (SL) · 融合型 Minecraft 启动器。\n阶段一:界面骨架 + 导航栈 + 主题 + 对话框 / 提示层。\n阶段二:HttpTransport 真实下载 + Mojang 元数据 + Java 扫描 + SHA-1 校验落盘。",
                ["下载引擎:有界并发 + 多源回退(官方 / BMCLAPI)+ SHA-1 终验", "数据落盘:libraries / assets / instances 目录(共享缓存)", "后续阶段:真实启动流水线、模组文件操作、账户与联机"]) });

        // —— 工具 ——
        Add("tools.online", "联机(VoxLink)", 0,
            () => new Pages.VoxLinkView { DataContext = new VoxLinkVM() });
        Add("tools.skins", "皮肤站", 0,
            () => new Pages.SkinStationView { DataContext = new SkinStationVM() });
        Add("tools.diag", "崩溃诊断", 0,
            () => new Pages.CrashDiagView { DataContext = new CrashDiagVM() });
        Add("tools.log", "应用日志", 0,
            () => new Pages.AppLogView { DataContext = new AppLogVM() });

        // —— 实例(从启动分区下钻) ——
        Add("inst.overview", "实例概览", 1,
            () => new Pages.InstanceOverviewView { DataContext = new InstanceOverviewVM() });
        Add("inst.mods", "模组管理", 1,
            () => new Pages.InstanceModsView { DataContext = new InstanceModsVM() });
        Add("inst.packs", "资源包", 1,
            () => new Pages.ModrinthView { DataContext = new ModrinthVM(Core.Net.ModrinthCategory.ResourcePacks) });
        Add("inst.shaders", "光影", 1,
            () => new Pages.ModrinthView { DataContext = new ModrinthVM(Core.Net.ModrinthCategory.Shaders) });
        Add("inst.saves", "存档", 1,
            () => new Pages.InstanceSavesView { DataContext = new InstanceSavesVM() });
        Add("inst.cfg", "实例设置", 1,
            () => new Pages.InstanceConfigView { DataContext = new InstanceConfigVM() });
        Add("inst.logs", "实例日志", 1,
            () => new Pages.AppLogView { DataContext = new AppLogVM(instanceMode: true) });
    }
}
