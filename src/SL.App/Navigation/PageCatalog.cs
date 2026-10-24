namespace SL.App.Navigation;

/// <summary>页面注册项:Key 即路由,Title 用于标题栏与面包屑,Hint 为占位说明,PageType 供 Frame 导航。</summary>
public sealed record PageSpec(string Key, string Title, string Hint, Type PageType);

public static class PageCatalog
{
    public static IReadOnlyDictionary<string, PageSpec> All { get; } = new Dictionary<string, PageSpec>
    {
        ["launch.home"] = new("launch.home", "启动面板", "账户选择、版本选择、启动按钮与全局状态区(Java 就绪/任务进行数)。", typeof(SL.App.Pages.PlaceholderPage)),
        ["launch.tasks"] = new("launch.tasks", "任务中心", "下载队列、单任务暂停/续传/取消/重试与批量操作(TaskCenterVM 已接入组合根)。", typeof(SL.App.Pages.PlaceholderPage)),
        ["dl.game"] = new("dl.game", "游戏版本", "Mojang 版本清单(官方/BMCLAPI 双源)按类型筛选与搜索。", typeof(SL.App.Pages.PlaceholderPage)),
        ["dl.loaders"] = new("dl.loaders", "加载器选择", "Fabric / Forge / NeoForge / Quilt 与对应版本匹配。", typeof(SL.App.Pages.PlaceholderPage)),
        ["dl.install"] = new("dl.install", "安装概览", "所选版本、加载器与组件清单确认,提交安装流水线。", typeof(SL.App.Pages.PlaceholderPage)),
        ["dl.pack"] = new("dl.pack", "模组仓库", "Modrinth 搜索、项目卡片与版本下载(图像走 ImageCacheService)。", typeof(SL.App.Pages.PlaceholderPage)),
        ["dl.shaders"] = new("dl.shaders", "光影仓库", "Modrinth shaders 分类检索与安装到当前实例。", typeof(SL.App.Pages.PlaceholderPage)),
        ["dl.packs"] = new("dl.packs", "资源包仓库", "Modrinth resource-packs 分类检索与安装。", typeof(SL.App.Pages.PlaceholderPage)),
        ["st.launch"] = new("st.launch", "外观与设置", "深浅色(含跟随系统)、4 套配色、5 种强调色与下载并发设置(Settings.Changed 已联动 ThemeManager)。", typeof(SL.App.Pages.PlaceholderPage)),
        ["st.global"] = new("st.global", "全局偏好", "窗口尺寸、代理与镜像源、界面语言、个人目录位置。", typeof(SL.App.Pages.PlaceholderPage)),
        ["st.about"] = new("st.about", "关于", "版本信息、开源许可与致谢。", typeof(SL.App.Pages.PlaceholderPage)),
        ["tools.online"] = new("tools.online", "联机", "联机(VoxLink):隧道创建、房间管理与延迟显示。", typeof(SL.App.Pages.PlaceholderPage)),
        ["tools.skins"] = new("tools.skins", "皮肤站", "authlib-injector 源接入、皮肤上传与预览。", typeof(SL.App.Pages.PlaceholderPage)),
        ["tools.diag"] = new("tools.diag", "崩溃诊断", "崩溃报告解析、特征匹配与修复建议。", typeof(SL.App.Pages.PlaceholderPage)),
        ["tools.log"] = new("tools.log", "应用日志", "实时滚动查看、级别过滤与导出。", typeof(SL.App.Pages.PlaceholderPage)),
        ["inst.overview"] = new("inst.overview", "实例概览", "图标、版本信息、快捷启动与操作入口。", typeof(SL.App.Pages.PlaceholderPage)),
        ["inst.mods"] = new("inst.mods", "模组管理", "启停、更新、删除与冲突提示。", typeof(SL.App.Pages.PlaceholderPage)),
        ["inst.packs"] = new("inst.packs", "资源包", "实例内资源包列表与导入。", typeof(SL.App.Pages.PlaceholderPage)),
        ["inst.shaders"] = new("inst.shaders", "光影", "实例内光影列表与 shaderpacks 目录操作。", typeof(SL.App.Pages.PlaceholderPage)),
        ["inst.saves"] = new("inst.saves", "存档", "世界列表、备份/恢复与 level.dat 信息展示。", typeof(SL.App.Pages.PlaceholderPage)),
        ["inst.cfg"] = new("inst.cfg", "实例设置", "JVM 参数、内存分配、分辨率与启动前命令。", typeof(SL.App.Pages.PlaceholderPage)),
        ["inst.logs"] = new("inst.logs", "实例日志", "最新日志/调试日志查看与检索。", typeof(SL.App.Pages.PlaceholderPage)),
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
