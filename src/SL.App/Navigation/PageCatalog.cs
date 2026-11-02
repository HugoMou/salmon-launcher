// SalmonLauncher (SL) — a Minecraft Java edition launcher
// Copyright (C) 2026 Hugo Mou
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
// PARTICULAR PURPOSE. See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with
// this program. If not, see <https://www.gnu.org/licenses/>.
//
// SPDX-License-Identifier: GPL-3.0-or-later
namespace SL.App.Navigation;

/// <summary>Page registration entry: Key is the route, Title feeds the title bar and breadcrumbs, Hint is placeholder copy, PageType is used by Frame navigation.</summary>
public sealed record PageSpec(string Key, string Title, string Hint, Type PageType);

public static class PageCatalog
{
    public static IReadOnlyDictionary<string, PageSpec> All { get; } = new Dictionary<string, PageSpec>
    {
        ["launch.home"] = new("launch.home", "启动面板", "账户选择、版本选择、启动按钮与全局状态区(Java 就绪/任务进行数)。", typeof(SL.App.Pages.HomePage)),
        ["launch.tasks"] = new("launch.tasks", "任务中心", "下载队列、单任务暂停/续传/取消/重试与批量操作(TaskCenterVM 已接入组合根)。", typeof(SL.App.Pages.TaskCenterPage)),
        ["dl.game"] = new("dl.game", "游戏版本", "Mojang 版本清单按类型筛选与搜索,选定后进入加载器选择。", typeof(SL.App.Pages.VersionListPage)),
        ["dl.loaders"] = new("dl.loaders", "加载器选择", "原版可直接安装;其余加载器在接入官方安装元数据前保持禁用。", typeof(SL.App.Pages.LoaderListPage)),
        ["dl.install"] = new("dl.install", "安装概览", "所选版本、加载器与组件清单确认,提交安装流水线。", typeof(SL.App.Pages.InstallPage)),
        ["dl.pack"] = new("dl.pack", "模组仓库", "Modrinth 检索与下载,安装到当前实例 mods 目录。", typeof(SL.App.Pages.RepoPage)),
        ["dl.shaders"] = new("dl.shaders", "光影仓库", "Modrinth shader 分类检索,安装到 shaderpacks 目录。", typeof(SL.App.Pages.RepoPage)),
        ["dl.packs"] = new("dl.packs", "资源包仓库", "Modrinth resourcepack 分类检索,安装到 resourcepacks 目录。", typeof(SL.App.Pages.RepoPage)),
        ["st.launch"] = new("st.launch", "外观与设置", "账户(设备码正版登录/离线切换)、深浅色(含跟随系统)、4 套配色、5 种强调色与下载并发设置。", typeof(SL.App.Pages.SettingsPage)),
        ["st.global"] = new("st.global", "全局偏好", "内存分配、窗口尺寸、JVM 参数、下载源与数据目录位置。", typeof(SL.App.Pages.GlobalSettingsPage)),
        ["st.theme"] = new("st.theme", "主题工坊", "从 Minecraft 方块纹理取色,生成并应用整套界面配色。", typeof(SL.App.Pages.ThemeWorkshopPage)),
        ["st.terms"] = new("st.terms", "用户协议", "使用条款、隐私说明与免责声明。", typeof(SL.App.Pages.TermsPage)),
        ["st.about"] = new("st.about", "关于", "版本信息、运行环境、能力概览与致谢。", typeof(SL.App.Pages.AboutPage)),
        ["tools.online"] = new("tools.online", "联机", "自建中继、局域网自动发现与手动添加中继服务器三种接入方式。", typeof(SL.App.Pages.MultiplayerPage)),
        ["tools.skins"] = new("tools.skins", "皮肤站", "按用户分栏查看已保存皮肤,并提供可旋转的 3D 方块模型预览。", typeof(SL.App.Pages.SkinStationPage)),
        ["tools.diag"] = new("tools.diag", "崩溃诊断", "崩溃报告解析、特征匹配与修复建议。", typeof(SL.App.Pages.CrashDiagPage)),
        ["tools.log"] = new("tools.log", "应用日志", "尾部读取、关键词过滤与日志目录直达。", typeof(SL.App.Pages.AppLogPage)),
        ["inst.overview"] = new("inst.overview", "实例概览", "实例信息、模组与存档计数、快捷启动与维护入口。", typeof(SL.App.Pages.InstanceOverviewPage)),
        ["inst.mods"] = new("inst.mods", "模组管理", "启停(改 .disabled 后缀)、删除与从本地导入。", typeof(SL.App.Pages.InstanceModsPage)),
        ["inst.packs"] = new("inst.packs", "资源包", "实例内 resourcepacks 列表与删除。", typeof(SL.App.Pages.InstanceFilesPage)),
        ["inst.shaders"] = new("inst.shaders", "光影", "实例内 shaderpacks 列表与删除。", typeof(SL.App.Pages.InstanceFilesPage)),
        ["inst.saves"] = new("inst.saves", "存档", "世界列表、打开世界目录与整包备份。", typeof(SL.App.Pages.InstanceFilesPage)),
        ["inst.cfg"] = new("inst.cfg", "实例设置", "内存分配、窗口尺寸、JVM 参数与实例目录维护。", typeof(SL.App.Pages.InstanceConfigPage)),
        ["inst.logs"] = new("inst.logs", "实例日志", "latest.log 尾部读取与关键词检索。", typeof(SL.App.Pages.InstanceLogsPage)),
    };

    public static PageSpec Get(string key) => All[key];
}

/// <summary>Static left-nav groups (same grouping as the old NavVM; WinUI NavigationView consumes them directly).</summary>
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
            new("st.theme", "主题工坊", "\uE790"),
            new("st.about", "关于", "\uE946"),
            new("st.terms", "用户协议", "\uE8A5"),
        ]),
    ];
}
