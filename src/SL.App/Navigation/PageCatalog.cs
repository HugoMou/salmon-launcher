namespace SL.App.Navigation;

/// <summary>页面注册项:Key 即路由,Title 用于标题栏与面包屑,PageType 供 Frame 导航。</summary>
public sealed record PageSpec(string Key, string Title, Type PageType);

public static class PageCatalog
{
    public static IReadOnlyDictionary<string, PageSpec> All { get; } = new Dictionary<string, PageSpec>
    {
        ["launch.home"] = new("launch.home", "启动面板", typeof(SL.App.Pages.LaunchHomePage)),
        ["launch.tasks"] = new("launch.tasks", "任务中心", typeof(SL.App.Pages.TaskCenterPage)),
        ["dl.game"] = new("dl.game", "游戏版本", typeof(SL.App.Pages.VersionListPage)),
        ["dl.loaders"] = new("dl.loaders", "加载器选择", typeof(SL.App.Pages.LoaderListPage)),
        ["dl.install"] = new("dl.install", "安装概览", typeof(SL.App.Pages.InstallSummaryPage)),
        ["dl.pack"] = new("dl.pack", "模组仓库", typeof(SL.App.Pages.ModrinthModsPage)),
        ["dl.shaders"] = new("dl.shaders", "光影仓库", typeof(SL.App.Pages.ModrinthShadersPage)),
        ["dl.packs"] = new("dl.packs", "资源包仓库", typeof(SL.App.Pages.ModrinthPacksPage)),
        ["st.launch"] = new("st.launch", "外观与设置", typeof(SL.App.Pages.SettingsPage)),
        ["st.global"] = new("st.global", "全局偏好", typeof(SL.App.Pages.GlobalPrefsPage)),
        ["st.about"] = new("st.about", "关于", typeof(SL.App.Pages.AboutPage)),
        ["tools.online"] = new("tools.online", "联机", typeof(SL.App.Pages.VoxLinkPage)),
        ["tools.skins"] = new("tools.skins", "皮肤站", typeof(SL.App.Pages.SkinStationPage)),
        ["tools.diag"] = new("tools.diag", "崩溃诊断", typeof(SL.App.Pages.CrashDiagPage)),
        ["tools.log"] = new("tools.log", "应用日志", typeof(SL.App.Pages.AppLogPage)),
        ["inst.overview"] = new("inst.overview", "实例概览", typeof(SL.App.Pages.InstanceOverviewPage)),
        ["inst.mods"] = new("inst.mods", "模组管理", typeof(SL.App.Pages.InstanceModsPage)),
        ["inst.packs"] = new("inst.packs", "资源包", typeof(SL.App.Pages.InstancePacksPage)),
        ["inst.shaders"] = new("inst.shaders", "光影", typeof(SL.App.Pages.InstanceShadersPage)),
        ["inst.saves"] = new("inst.saves", "存档", typeof(SL.App.Pages.InstanceSavesPage)),
        ["inst.cfg"] = new("inst.cfg", "实例设置", typeof(SL.App.Pages.InstanceConfigPage)),
        ["inst.logs"] = new("inst.logs", "实例日志", typeof(SL.App.Pages.InstanceLogsPage)),
    };

    public static PageSpec Get(string key) => All[key];
}

/// <summary>左导航静态分组(与旧版 NavVM 分组一致,WinUI NavigationView 直接消费)。</summary>
public static class NavGroups
{
    public sealed record Item(string Key, string Label, string Glyph);

    public sealed record Group(string Title, IReadOnlyList<Item> Items);

    public static IReadOnlyList<Group> All { get; } =
    [
        new("启动", [
            new("launch.home", "启动面板", "\uE80F"),
            new("launch.tasks", "任务中心", "\uE823"),
        ]),
        new("实例", [
            new("inst.overview", "概览", "\uE7FC"),
            new("inst.mods", "模组管理", "\uE7B8"),
            new("inst.saves", "存档", "\uE74E"),
            new("inst.packs", "资源包", "\uE8F1"),
            new("inst.shaders", "光影", "\uE706"),
            new("inst.logs", "实例日志", "\uE8A5"),
            new("inst.cfg", "实例设置", "\uE713"),
        ]),
        new("下载", [
            new("dl.game", "游戏版本", "\uE7FC"),
            new("dl.pack", "模组仓库", "\uE7B8"),
            new("dl.shaders", "光影仓库", "\uE706"),
            new("dl.packs", "资源包仓库", "\uE8F1"),
        ]),
        new("工具", [
            new("tools.online", "联机", "\uE716"),
            new("tools.skins", "皮肤站", "\uE77B"),
            new("tools.diag", "崩溃诊断", "\uE7BA"),
            new("tools.log", "应用日志", "\uE8A5"),
        ]),
        new("设置", [
            new("st.launch", "外观与设置", "\uE713"),
            new("st.global", "全局偏好", "\uE90F"),
            new("st.about", "关于", "\uE946"),
        ]),
    ];
}
