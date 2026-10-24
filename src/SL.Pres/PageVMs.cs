using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SL.Core.Transfers;
using SL.Pres.Dialogs;
using SL.Pres.Infrastructure;
using SL.Pres.Notify;

namespace SL.Pres;

/// <summary>打开资源管理器的公共方法。</summary>
public static class OpenHelpers
{
    public static void OpenUri(string uri)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Core.Logging.AppLog.Warn("OpenUri", ex.Message);
        }
    }

    public static void OpenExplorer(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppServices.Notify.Publish(NotifyKind.Warning, $"无法打开目录:{ex.Message}");
        }
    }
}

/// <summary>启动入口(阶段三:真实启动流水线)。</summary>
public static class LaunchFlow
{
    private static long _lastStepRaiseMs;

    public static async Task AttemptAsync()
    {
        Core.Logging.AppLog.Info("Launch", "AttemptAsync 进入");
        var instance = Core.Launch.LaunchPipeline.LatestInstanceId();
        Core.Logging.AppLog.Info("Launch", "LatestInstanceId=" + (instance ?? "(null)"));
        if (instance is null)
        {
            await AppServices.Dialogs.AlertAsync("启动游戏",
                "尚未安装任何版本。\n请到「下载 → 游戏版本」完成一次安装后再启动。",
                Dialogs.MsgMark.Warning);
            return;
        }

        var ok = await AppServices.Dialogs.ConfirmAsync("启动游戏",
            $"以离线账户「Rio」启动 Minecraft {instance}?\n\n阶段三流水线:库校验 → natives 解压 → 资产差量补齐 → Java 定位 → 参数组装 → 进程启动。",
            MsgMark.Question, okLabel: "启动");
        Core.Logging.AppLog.Info("Launch", "ConfirmAsync 结果=" + ok);
        if (!ok)
            return;

        _lastStepRaiseMs = 0;

        void OnStep(string message)
        {
            var now = Environment.TickCount64;
            var important = message.Contains("PID", StringComparison.Ordinal)
                            || message.Contains("已启动", StringComparison.Ordinal)
                            || message.Contains("退出", StringComparison.Ordinal);
            if (!important && now - _lastStepRaiseMs < 500)
                return;
            _lastStepRaiseMs = now;
            AppServices.Notify.Publish(NotifyKind.Info, message);
        }

        void OnJavaProgress(long received, long total)
            => AppServices.Notify.Publish(NotifyKind.Info,
                $"正在获取 Java JDK:{received / (1024.0 * 1024.0):F0} MB / {total / (1024.0 * 1024.0):F0} MB");

        Core.Launch.LaunchPipeline.Step += OnStep;
        Core.Launch.JavaFetch.DownloadProgress += OnJavaProgress;
        Core.Launch.LaunchPipeline.GameExited += OnGameExited;
        Core.Launch.LaunchResult result;
        try
        {
            // 账户身份:msa 会话存在且启用 → 走正版令牌(过期自动续签);否则离线
            Core.Launch.LaunchPipeline.LaunchIdentity? identity = null;
            var cfg = AppServices.Settings.Current;
            if (cfg.AccountType == "msa")
            {
                var session = Core.Account.MsAuth.LoadSession();
                if (session is null)
                {
                    AppServices.Notify.Publish(NotifyKind.Warning, "微软正版会话不存在,已按离线模式启动;可在皮肤站重新登录");
                }
                else
                {
                    session = await Core.Account.MsAuth
                        .EnsureFreshAsync(session, step => AppServices.Notify.Publish(NotifyKind.Info, "正版:" + step))
                        .ConfigureAwait(true);
                    if (session.MinecraftValid)
                    {
                        Core.Account.MsAuth.SaveSession(session);
                        identity = new Core.Launch.LaunchPipeline.LaunchIdentity(
                            session.Name, session.Uuid, session.MinecraftAccessToken, "msa");
                    }
                    else
                    {
                        AppServices.Notify.Publish(NotifyKind.Warning, "正版令牌续签失败,已按离线模式启动");
                    }
                }
            }

            result = await Core.Launch.LaunchPipeline
                .StartAsync(instance, AppServices.Settings.Current.AccountName, memoryMb: 4096,
                    CancellationToken.None, identity)
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Core.Logging.AppLog.Error("LaunchFlow", ex);
            result = new Core.Launch.LaunchResult(false, 0, "启动流水线异常:" + ex.Message);
        }
        finally
        {
            Core.Launch.LaunchPipeline.Step -= OnStep;
            Core.Launch.JavaFetch.DownloadProgress -= OnJavaProgress;
            Core.Launch.LaunchPipeline.GameExited -= OnGameExited;
        }

        if (result.Success)
        {
            AppServices.Notify.Publish(NotifyKind.Success,
                $"Minecraft {instance} 已启动(PID {result.ProcessId}),运行日志在实例目录");
            // 可能刚装好了 JDK 或退出过游戏:后台重扫 Java,刷新面板显示
            _ = Infrastructure.JavaEnv.RescanAsync();
        }
        else
        {
            Core.Logging.AppLog.Info("Launch", "启动失败:" + result.Error);
            await AppServices.Dialogs.AlertAsync("启动失败", result.Error ?? "未知错误", MsgMark.Error);
        }
    }

    /// <summary>全局常驻:游戏进程退出诊断提示(阶段六深度诊断由此接出)。</summary>
    private static void OnGameExited(int exitCode)
    {
        AppServices.Notify.Publish(exitCode == 0 ? NotifyKind.Info : NotifyKind.Warning,
            exitCode == 0 ? "游戏正常退出" : $"游戏异常退出(ExitCode {exitCode}),可在工具 → 崩溃诊断查看");
    }
}

// ======================================================== 启动面板(阶段四 · 真实实例数据)

public sealed class LaunchHomeVM : VmBase, Infrastructure.IRefreshable
{
    private static Core.Instances.InstanceInfo? CurrentInstance()
        => Core.Instances.InstanceRegistry.Latest();

    public string InstanceName
    {
        get
        {
            var inst = CurrentInstance();
            return inst is null ? "尚未安装版本" : $"{inst.InstanceId}";
        }
    }    public string InstanceBadge => CurrentInstance() is null ? "去「下载 → 游戏版本」安装" : "已安装 · 事实为准";

    /// <summary>hero 副标题:版本类型 + Java 状态。</summary>
    public string HeroLine
    {
        get
        {
            var inst = CurrentInstance();
            if (inst is null)
                return "完成一次安装后,这里会显示该实例的自动配置摘要。";
            return $"原版 · {inst.InstanceId} · {JavaEnv.StatusText}";
        }
    }
    public string JavaStatus => Infrastructure.JavaEnv.StatusText;
    public string JavaDetail => Infrastructure.JavaEnv.DetailText;
    public string MemText => "4096 MB(全局默认)";
    public string ModsText
    {
        get
        {
            var inst = CurrentInstance();
            if (inst is null)
                return "0 个模组(未安装实例)";
            var mods = Core.Instances.InstanceRegistry.ListMods(inst.InstanceId);
            return $"{mods.Count(m => m.Enabled)} 个模组启用 · {mods.Count(m => !m.Enabled)} 个停用";
        }
    }
    public string SaveText
    {
        get
        {
            var inst = CurrentInstance();
            if (inst is null)
                return "无实例";
            return $"实例体积 {inst.SizeMb} MB · 安装于 {inst.InstalledAt:MM-dd HH:mm}";
        }
    }
    public string AccountText => "离线账户 · " + AppServices.Settings.Current.AccountName;

    public string SummaryLine => AppServices.TaskCenter.SummaryText;
    public string ConcurrencyNote => AppServices.TaskCenter.ConcurrencyNote;

    public LaunchHomeVM()
    {
        AppServices.TaskCenter.SummaryChanged += () =>
        {
            On(nameof(SummaryLine));
            On(nameof(ConcurrencyNote));
        };
        AppServices.Settings.Changed += _ =>
        {
            On(nameof(ConcurrencyNote));
            On(nameof(AccountText));
        };
        Infrastructure.JavaEnv.Changed += () => On(nameof(JavaStatus));
    }

    public ICommand StartCommand { get; } = new RelayCommand(() => _ = LaunchFlow.AttemptAsync());
    public ICommand EditConfigCommand { get; } = new RelayCommand(() => AppServices.Nav.Open("inst.overview"));
    public ICommand OpenFolderCommand { get; } = new RelayCommand(() =>
    {
        var inst = CurrentInstance();
        var dir = inst is null
            ? Core.Boot.Runtime.InstancesDir
            : Core.Instances.InstanceRegistry.InstanceDirOf(inst.InstanceId);
        OpenHelpers.OpenExplorer(dir);
    });
    public ICommand SpawnTaskCommand { get; } = new RelayCommand(
        () => DemoTasks.SpawnSample($"演示资源 {DateTime.Now:HHmmss}.zip"));
    public ICommand OpenTasksCommand { get; } = new RelayCommand(() => AppServices.Nav.Open("launch.tasks"));

    /// <summary>再入本页时刷新实例相关事实(hero 卡片各字段均为 getter,推一次变更即可)。</summary>
    public void RefreshData()
    {
        On(nameof(InstanceName));
        On(nameof(InstanceBadge));
        On(nameof(HeroLine));
        On(nameof(ModsText));
        On(nameof(SaveText));
        On(nameof(AccountText));
        On(nameof(MemText));
    }
}

// ======================================================== 实例概览(阶段四 · 真实实例数据)

public sealed class InstanceOverviewVM : VmBase, Infrastructure.IRefreshable
{
    private static Core.Instances.InstanceInfo? CurrentInstance()
        => Core.Instances.InstanceRegistry.Latest();

    private static string InstanceIdSafe()
        => CurrentInstance()?.InstanceId ?? "";

    public string InstanceName => CurrentInstance() is null ? "尚未安装版本" : InstanceIdSafe();
    public string LoaderBadge => CurrentInstance() is null ? "—" : "原版(官方元数据)";
    public string MemText => "4096 MB";
    public string ModsText
    {
        get
        {
            var inst = CurrentInstance();
            if (inst is null)
                return "0 个";
            var mods = Core.Instances.InstanceRegistry.ListMods(inst.InstanceId);
            return $"{mods.Count(m => m.Enabled)} 个启用 / {mods.Count(m => !m.Enabled)} 个停用";
        }
    }
    public string SavesText => $"实例体积 {CurrentInstance()?.SizeMb ?? 0} MB";
    public string LastLaunch
    {
        get
        {
            var inst = CurrentInstance();
            if (inst is null)
                return "—";
            var days = (DateTimeOffset.Now - inst.InstalledAt).TotalDays;
            return days < 1 ? "今天" : days < 2 ? "昨天" : $"{(int)days} 天前安装";
        }
    }
    public string JavaHint => Infrastructure.JavaEnv.StatusText;
    public string Overview => CurrentInstance() is null
        ? "实例目录为空:完成一次下载安装后,本页以磁盘事实展示该实例。"
        : $"实例位于 instances\\{InstanceIdSafe()}(隔离目录:meta/versions/mods/logs 分区)。";

    public InstanceOverviewVM()
    {
        Infrastructure.JavaEnv.Changed += () => On(nameof(JavaHint));
    }

    public void RefreshData()
    {
        On(nameof(InstanceName));
        On(nameof(LoaderBadge));
        On(nameof(ModsText));
        On(nameof(SavesText));
        On(nameof(LastLaunch));
        On(nameof(Overview));
        On(nameof(JavaHint));
    }

    public ICommand StartCommand { get; } = new RelayCommand(() => _ = LaunchFlow.AttemptAsync());
    public ICommand OpenModsCommand { get; } = new RelayCommand(() => AppServices.Nav.Open("inst.mods"));
    public ICommand BackHomeCommand { get; } = new RelayCommand(() => AppServices.Nav.Back());
}

// ======================================================== 占位页

public sealed class PageSimpleVM : VmBase
{
    public string Title { get; }
    public string Summary { get; }
    public IReadOnlyList<string> Items { get; }

    public PageSimpleVM(string title, string summary, IReadOnlyList<string> items)
    {
        Title = title;
        Summary = summary;
        Items = items;
    }
}

// ======================================================== 模组管理(阶段四 · 真实文件操作)

public sealed class ModRowVM : VmBase
{
    private bool _enabled;

    /// <summary>显示名(去 .disabled 的文件名主体)。</summary>
    public string Name { get; }
    /// <summary>真实文件名(操作目标)。</summary>
    public string FileName { get; }
    public string BaseName { get; }
    public string RealFileName => FileName;
    public string Version { get; }
    public string Loader { get; }
    public string LoaderBadge => Loader;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (Set(ref _enabled, value))
                On(nameof(StateBadge));
        }
    }

    public string StateBadge => _enabled ? "启用" : "停用";

    public ModRowVM(Core.Instances.ModInfo info)
    {
        Name = info.BaseName;
        BaseName = info.BaseName;
        FileName = info.FileName;
        Version = info.SizeBytes >= 1 << 20
            ? $"{info.SizeBytes / (1024.0 * 1024.0):F1} MB"
            : $"{info.SizeBytes / 1024.0:F0} KB";
        Loader = info.ModifiedAt.ToString("MM-dd HH:mm");
        _enabled = info.Enabled;
    }
}

public sealed class InstanceModsVM : VmBase, Infrastructure.IRefreshable
{
    private string _instance;

    public ObservableCollection<ModRowVM> Rows { get; } = [];

    public string SubTitle => _instance.Length == 0
        ? "尚未安装任何版本;先到下载页安装一个版本再管理模组。"
        : $"实例 {_instance} · mods 目录真实文件;启停(.jar ↔ .jar.disabled)/删除/本地导入即时生效。";

    public InstanceModsVM()
    {
        _instance = Core.Launch.LaunchPipeline.LatestInstanceId() ?? "";
        Reload();

        // 启停联动:CheckBox 设 Enabled → 文件重命名,失败回滚
        foreach (var row in Rows)
            row.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(ModRowVM.StateBadge))
                    return;
                try
                {
                    var enabled = Core.Instances.InstanceRegistry.ToggleMod(_instance, row.FileName);
                    AppServices.Notify.Publish(NotifyKind.Success,
                        $"{(enabled ? "已启用" : "已停用")}:{row.BaseName}");
                }
                catch (Exception ex)
                {
                    // 文件层失败(同名冲突等):回滚视觉
                    row.Enabled = !row.Enabled;
                    AppServices.Notify.Publish(NotifyKind.Error, "启停失败:" + ex.Message);
                }
            };

        DeleteCommand = new RelayCommand<ModRowVM?>(async row =>
        {
            if (row is null)
                return;
            var ok = await AppServices.Dialogs.ConfirmAsync("删除模组",
                $"将删除 {row.FileName}?\nmods 目录中该文件将被永久移除。", MsgMark.Warning, okLabel: "删除");
            if (!ok)
                return;

            try
            {
                Core.Instances.InstanceRegistry.DeleteMod(_instance, row.FileName);
                Rows.Remove(row);
                AppServices.Notify.Publish(NotifyKind.Success, $"已删除 {row.BaseName}");
            }
            catch (Exception ex)
            {
                AppServices.Notify.Publish(NotifyKind.Error, "删除失败:" + ex.Message);
            }
        });

        AddModCommand = new RelayCommand(async () =>
        {
            var pick = await AppServices.Dialogs.ChooseAsync("添加模组", "选择来源",
            [
                "Modrinth 搜索(新页面)",
                "从本地导入 .jar",
                "演示任务(占位传输)",
            ]);
            if (pick is null)
                return;

            if (pick == 2)
            {
                DemoTasks.SpawnSample("modpack-demo.jar", TransferPriority.Library);
                return;
            }
            if (pick == 1)
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "导入模组",
                    Filter = "Minecraft 模组|*.jar|任何文件|*.*",
                };
                if (dlg.ShowDialog() is true)
                {
                    try
                    {
                        var name = Core.Instances.InstanceRegistry.ImportMod(_instance, dlg.FileName);
                        AppServices.Notify.Publish(NotifyKind.Success, $"已导入 {name}(复制,不动原件)");
                        Reload();
                    }
                    catch (Exception ex)
                    {
                        AppServices.Notify.Publish(NotifyKind.Error, "导入失败:" + ex.Message);
                    }
                }
                return;
            }

            // Modrinth 搜索
            AppServices.Nav.Open("dl.pack");
        });
    }

    private void Reload()
    {
        Rows.Clear();
        if (_instance.Length == 0)
            return;
        foreach (var m in Core.Instances.InstanceRegistry.ListMods(_instance))
            Rows.Add(new ModRowVM(m));
    }

    /// <summary>再入本页:重解析最近实例并重读 mods 目录(装好模组后回页可见新文件)。</summary>
    public void RefreshData()
    {
        var previous = _instance;
        _instance = Core.Launch.LaunchPipeline.LatestInstanceId() ?? "";
        var subtitleChanged = previous != _instance;
        Reload();
        if (subtitleChanged)
            On(nameof(SubTitle));
    }

    public ICommand DeleteCommand { get; private set; } = null!;
    public ICommand AddModCommand { get; private set; } = null!;
}

// ======================================================== 实例设置

public sealed class InstanceConfigVM : VmBase
{
    private string _memory = "4096 MB";
    public string Memory
    {
        get => _memory;
        set
        {
            if (Set(ref _memory, value))
                AppServices.Notify.Publish(NotifyKind.Info, "实例内存覆盖已记录(全局默认与实例级覆盖的合并逻辑在阶段三生效)");
        }
    }

    public string JavaText => "使用启动器托管的 Java 21(自动下载,阶段二)";
    public string JvmHint => "JVM 参数模板与 GC 策略在阶段三接入";

    public ICommand OpenFolderCommand { get; } = new RelayCommand(
        () => OpenHelpers.OpenExplorer(Core.Boot.Runtime.InstancesDir));
}

// ======================================================== 下载家族

public static class WizardPlan
{
    public static string Version = "1.21.4";
    public static string Loader = "Fabric 0.15.11";

    // —— 阶段二:Mojang 元数据链接(进入安装概览时携带) ——
    public static string VersionUrl = "";
    public static string VersionSha1 = "";
    public static Core.Net.MojangMeta.VersionDetail? Detail;

    /// <summary>主源 URL 映射到 BMCLAPI 镜像(国内网络加速,供引擎多源回退)。</summary>
    public static string? MirrorOf(string url)
    {
        if (string.IsNullOrEmpty(url))
            return null;
        return url switch
        {
            // 官方
            var u when u.Contains("piston-meta.mojang.com", StringComparison.Ordinal)
                => u.Replace("piston-meta.mojang.com", "bmclapi2.bangbang93.com", StringComparison.Ordinal),
            var u when u.Contains("piston-data.mojang.com", StringComparison.Ordinal)
                => u.Replace("piston-data.mojang.com", "bmclapi2.bangbang93.com", StringComparison.Ordinal),
            var u when u.Contains("launchermeta.mojang.com", StringComparison.Ordinal)
                => u.Replace("launchermeta.mojang.com", "bmclapi2.bangbang93.com", StringComparison.Ordinal),
            var u when u.Contains("launcher.mojang.com", StringComparison.Ordinal)
                => u.Replace("launcher.mojang.com", "bmclapi2.bangbang93.com", StringComparison.Ordinal),
            var u when u.Contains("resources.download.minecraft.net", StringComparison.Ordinal)
                => u.Replace("https://resources.download.minecraft.net", "https://bmclapi2.bangbang93.com/assets", StringComparison.Ordinal),
            _ => null,
        };
    }
}

public sealed class PickRowVM : VmBase
{
    public string Label { get; }
    public string? Badge { get; }
    public bool Disabled { get; }

    public PickRowVM(string label, string? badge, bool disabled, Action onSelect)
    {
        Label = label;
        Badge = badge;
        Disabled = disabled;
        SelectCommand = new RelayCommand(onSelect, () => !Disabled);
    }

    public string Display => Disabled ? $"{Label}(暂不支持)" : Label;

    public ICommand SelectCommand { get; }
}

public sealed class VersionListVM : VmBase
{
    private IReadOnlyList<PickRowVM> _items =
    [
        new("正在读取 Mojang 版本清单…", null, true, () => { }),
    ];

    public IReadOnlyList<PickRowVM> Items
    {
        get => _items;
        private set => Set(ref _items, value);
    }

    public VersionListVM()
    {
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var versions = (await Core.Net.MojangMeta
                .GetVersionsAsync(forceRefresh: false, CancellationToken.None)
                .ConfigureAwait(true)).ToList();

            // 展示策略:最新 40 个稳定版 + 最新 12 个快照/其他类型(保证版本选择触手可及)
            var releases = versions.Where(v => v.Type == "release").Take(40).ToList();
            var others = versions.Where(v => v.Type != "release").Take(12).ToList();

            Items = releases.Concat(others)
                .Select(v => new PickRowVM(v.Id, v.Badge, false, () => Choose(v)))
                .ToArray();
            AppServices.Notify.Publish(NotifyKind.Info,
                $"已载入官方版本清单({versions.Count} 个版本 · 展示最新 {releases.Count} 个稳定版 + {others.Count} 个快照)");
        }
        catch (Exception ex)
        {
            Items =
            [
                new("1.21.4", "稳定版·离线", false, () => ChooseId("1.21.4")),
                new("1.21.3", "稳定版·离线", false, () => ChooseId("1.21.3")),
                new("1.20.6", "稳定版·离线", false, () => ChooseId("1.20.6")),
                new("1.19.2", "稳定版·离线", false, () => ChooseId("1.19.2")),
            ];
            AppServices.Notify.Publish(NotifyKind.Warning, $"版本清单获取失败({ex.Message}),已回退离线列表");
        }
    }

    private static void Choose(Core.Net.MojangMeta.VersionEntry v)
    {
        WizardPlan.Version = v.Id;
        WizardPlan.VersionUrl = v.Url;
        WizardPlan.VersionSha1 = v.Sha1 ?? "";
        AppServices.Notify.Publish(NotifyKind.Info, $"已选择 {v.Id},下一步选择加载器");
        AppServices.Nav.Open("dl.loaders");
    }

    private static void ChooseId(string id)
    {
        // 离线兜底:没有具体版本 JSON 链接,安装概览会提示重试
        WizardPlan.Version = id;
        WizardPlan.VersionUrl = "";
        WizardPlan.VersionSha1 = "";
        AppServices.Notify.Publish(NotifyKind.Info, $"已选择 {id}(离线模式,网络恢复后请重新进入本页刷新链接)");
        AppServices.Nav.Open("dl.loaders");
    }
}

public sealed class LoaderListVM : VmBase
{
    public IReadOnlyList<PickRowVM> Items { get; } =
    [
        new("无加载器(原版)", null, false, () => Choose("原版")),
        new("Fabric", "0.15.11", false, () => Choose("Fabric 0.15.11")),
        new("NeoForge", "21.x 演示", false, () => Choose("NeoForge 21.x")),
        new("Forge", "注册流程在阶段二接入", false, () => Choose("Forge(阶段二)")),
        new("Quilt", "阶段四扩展模块", true, () => { }),
    ];

    private static void Choose(string loader)
    {
        WizardPlan.Loader = loader;
        AppServices.Notify.Publish(NotifyKind.Info, $"加载器:{loader},即将进入安装概览");
        AppServices.Nav.Open("dl.install");
    }
}

public sealed class InstallSummaryVM : VmBase
{
    private IReadOnlyList<string> _planLines = ["正在读取版本元数据…"];

    private string _stageNote = "读取 Mojang 版本 JSON 中…";
    private bool _ready;

    public string PlanTitle => $"安装 {WizardPlan.Loader} → Minecraft {WizardPlan.Version}";

    public InstallSummaryVM()
    {
        BeginCommand = new RelayCommand(() => _ = BeginAsync(), () => _ready);
        _ = LoadPlanAsync();
    }

    public IReadOnlyList<string> PlanLines
    {
        get => _planLines;
        private set => Set(ref _planLines, value);
    }

    public string StageNote
    {
        get => _stageNote;
        private set => Set(ref _stageNote, value);
    }

    public ICommand BeginCommand { get; }

    // ------------------------------------------------------------ 计划读取

    private async Task LoadPlanAsync()
    {
        _ready = false;
        Core.Net.MojangMeta.VersionDetail? detail = null;
        var noteExtra = "";

        if (!string.IsNullOrEmpty(WizardPlan.VersionUrl))
        {
            try
            {
                detail = await Core.Net.MojangMeta
                    .GetVersionAsync(WizardPlan.VersionUrl, CancellationToken.None)
                    .ConfigureAwait(true);
                WizardPlan.Detail = detail;
            }
            catch (Exception ex)
            {
                noteExtra = $"元数据读取失败:{ex.Message}。";
            }
        }
        else
        {
            noteExtra = "当前处于离线选择,没有版本链接;请返回上一页重新进入以刷新。";
        }

        if (detail?.Client is null)
        {
            PlanLines =
            [
                $"Minecraft {WizardPlan.Version}({WizardPlan.Loader})",
                noteExtra + "暂无法生成下载计划(网络不可达)。",
                "恢复网络后重新进入本页即可自动重读。",
            ];
            StageNote = "阶段二:核心 / 库 / 索引 基于 Mojang 元数据真实 HTTP 下载 + SHA-1 校验落盘;资源对象与启动流水线在阶段三接入。";
            On(nameof(PlanLines));
            return;
        }

        var natives = detail.Libraries.Count(l => l.IsNative);
        PlanLines =
        [
            $"版本元数据 {detail.Id}.json({FmtKb(WizardPlan.VersionUrl)}  · SHA-1 校验)",
            $"游戏核心 {detail.Id}.jar(约 {FmtMb(detail.Client.Size)} · 官方主源 + BMCLAPI 镜像)",
            $"库文件 {detail.Libraries.Count} 个(约 {FmtMb(detail.LibraryBytes)},含 {natives} 个 Windows natives)",
            $"资源索引 assets/{detail.AssetId}.json(约 {FmtMb(detail.AssetIndex?.Size ?? -1)})",
            "资源对象将在阶段三按索引差量补齐(本阶段不下载)",
        ];
        StageNote = "阶段二:以上项目以 HttpTransport 真实落盘至 SalmonLauncher 数据目录(库 / assets 为共享目录),逐文件 SHA-1 终验;失败自动重试并切换 BMCLAPI 镜像。";
        On(nameof(PlanLines));
        _ready = true;
        CommandManager.InvalidateRequerySuggested();
    }

    // ------------------------------------------------------------ 开始安装

    private Task BeginAsync()
    {
        var detail = WizardPlan.Detail;
        if (detail?.Client is null)
            return Task.CompletedTask;

        var id = detail.Id;
        var instanceMetaDir = Path.Combine(Core.Boot.Runtime.InstancesDir, id, "meta");
        var versionDir = Path.Combine(Core.Boot.Runtime.InstancesDir, id, "versions");
        var indexesDir = Path.Combine(Core.Boot.Runtime.AssetsDir, "indexes");

        // 1. 版本元数据 JSON
        if (!string.IsNullOrEmpty(WizardPlan.VersionUrl))
            Submit("元数据 " + id + ".json", WizardPlan.VersionUrl,
                WizardPlan.VersionSha1, -1,
                Path.Combine(instanceMetaDir, id + ".json"),
                TransferPriority.CoreGameFile);

        // 2. 游戏核心 jar
        Submit($"游戏核心 {id}.jar", detail.Client.Url, detail.Client.Sha1, detail.Client.Size,
            Path.Combine(versionDir, id + ".jar"), TransferPriority.CoreGameFile);

        // 3. 库文件(natives 一并入库,阶段三启动准备时解压)
        foreach (var lib in detail.Libraries)
            Submit("库 " + Path.GetFileName(lib.RelativePath), lib.Url, lib.Sha1, lib.Size,
                Path.Combine(Core.Boot.Runtime.LibrariesDir, lib.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                lib.IsNative ? TransferPriority.Library : TransferPriority.Library);

        // 4. 资源索引
        if (detail.AssetIndex is not null)
            Submit($"资源索引 {detail.AssetId}.json", detail.AssetIndex.Url, detail.AssetIndex.Sha1, detail.AssetIndex.Size,
                Path.Combine(indexesDir, detail.AssetId + ".json"), TransferPriority.AssetIndex);

        AppServices.Notify.Publish(NotifyKind.Success,
            $"已提交 {id} 安装任务({2 + detail.Libraries.Count + (detail.AssetIndex is null ? 0 : 1)} 个),可在任务中心查看进度");
        AppServices.Nav.Open("launch.tasks");
        return Task.CompletedTask;
    }

    private static void Submit(string name, string url, string? sha1, long size, string dest, int priority)
    {
        var sources = new List<TransferSourceSpec> { new(url, isMirror: false) };
        var mirror = WizardPlan.MirrorOf(url);
        if (mirror is not null)
            sources.Add(new(mirror, isMirror: true));

        AppServices.Transfers.Submit(new TransferSpec
        {
            DisplayName = name,
            Kind = TransferKind.Http,
            Priority = priority,
            BytesTotal = size,
            ExpectedSha1 = string.IsNullOrWhiteSpace(sha1) ? null : sha1,
            DestPath = dest,
            Sources = sources,
        });
    }

    private static string FmtMb(long bytes)
        => bytes < 0 ? "未知" : bytes >= 1 << 20 ? $"{bytes / (1024.0 * 1024.0):F1} MB" : $"{bytes / 1024.0:F0} KB";

    private static string FmtKb(string url) => "~1MB JSON";
}

public sealed class DownloadItemVM : VmBase
{
    public string Title { get; }
    public string Meta { get; }
    public string SizeText { get; }

    public DownloadItemVM(string title, string meta, string sizeText)
    {
        Title = title;
        Meta = meta;
        SizeText = sizeText;
        InstallCommand = new RelayCommand(() => _ = InstallAsync());
    }

    private async Task InstallAsync()
    {
        var ok = await AppServices.Dialogs.ConfirmAsync("安装",
            $"下载「{Title}」并安装到当前实例?\n文件:约 {SizeText}", MsgMark.Question, okLabel: "安装");
        if (!ok)
            return;

        DemoTasks.SpawnSample(Title, TransferPriority.Library);
    }

    public ICommand InstallCommand { get; }
}

public sealed class DownloadListVM : VmBase
{
    public string Title { get; }
    public string Summary { get; }
    public IReadOnlyList<DownloadItemVM> Items { get; }

    public DownloadListVM(string title, string summary, IReadOnlyList<DownloadItemVM> items)
    {
        Title = title;
        Summary = summary;
        Items = items;
    }
}

// ======================================================== Modrinth(阶段四 · 真实检索 + 安装)

public sealed class ModrinthHitVM : VmBase
{
    public string Slug { get; }
    public string Title { get; }
    public string Description { get; }
    public string Meta { get; }
    public ICommand InstallCommand { get; }

    public ModrinthHitVM(Core.Net.Modrinth.ProjectHit hit, Action<ModrinthHitVM> install, bool installed = false)
    {
        Slug = hit.Slug;
        Title = hit.Title;
        Description = hit.Description;
        Meta = $"{hit.Author} · {hit.DownloadsText} 下载"
               + (installed ? " · 已安装" : "")
               + (hit.Categories.Count > 0 ? " · " + string.Join("/", hit.Categories) : "");
        InstallCommand = new RelayCommand(() => install(this));
    }
}

public sealed class ModrinthVM : VmBase, Infrastructure.IRefreshable
{
    private readonly Core.Net.ModrinthCategory _category;
    private string _searchText = "";
    private string _statusText;
    private bool _busy;

    public ObservableCollection<ModrinthHitVM> Rows { get; } = [];

    public string SearchText
    {
        get => _searchText;
        set => Set(ref _searchText, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string PageTitle => _category switch
    {
        Core.Net.ModrinthCategory.Shaders => "光影仓库 · Modrinth",
        Core.Net.ModrinthCategory.ResourcePacks => "资源包仓库 · Modrinth",
        _ => "模组仓库 · Modrinth",
    };

    public string InstanceHint
    {
        get
        {
            var inst = Core.Instances.InstanceRegistry.Latest();
            var dest = _category switch
            {
                Core.Net.ModrinthCategory.Shaders => "shaderpacks",
                Core.Net.ModrinthCategory.ResourcePacks => "resourcepacks",
                _ => "mods",
            };
            return inst is null
                ? "尚未安装版本 → 先装好一个版本再回来,文件将按最近实例落盘。"
                : $"目标实例:{inst.InstanceId} · 完成后进入 {dest} 目录生效。";
        }
    }

    public ICommand SearchCommand { get; }

    public ModrinthVM(Core.Net.ModrinthCategory category = Core.Net.ModrinthCategory.Mods)
    {
        _category = category;
        _statusText = category switch
        {
            Core.Net.ModrinthCategory.Shaders =>
                "浏览通用光影包(Complementary、BSL 等);依赖 Iris 模组,可在模组仓库安装。",
            Core.Net.ModrinthCategory.ResourcePacks =>
                "浏览资源包;资源包与本体版本基本无关,下载后游戏内「选项 → 资源包」启用。",
            _ => "输入关键词后点「搜索」(结果按当前实例版本 + Fabric 自动兼容过滤)。",
        };
        SearchCommand = new RelayCommand(() => _ = SearchAsync(), () => !_busy);
    }

    /// <summary>再入本页:目标实例可能已变(刚装了新版本),刷新提示;不做昂贵网络请求。</summary>
    public void RefreshData() => On(nameof(InstanceHint));

    private async Task SearchAsync()
    {
        var inst = Core.Instances.InstanceRegistry.Latest();
        if (inst is null)
        {
            AppServices.Notify.Publish(NotifyKind.Warning, "先安装一个版本,再回来装资源(按最近实例落盘)");
            return;
        }

        _busy = true;
        StatusText = "检索 Modrinth…";
        try
        {
            IReadOnlyList<Core.Net.Modrinth.ProjectHit> hits;
            switch (_category)
            {
                case Core.Net.ModrinthCategory.Shaders:
                    hits = await Core.Net.Modrinth.SearchByFacetsAsync(
                        _searchText.Trim(), "[[\"project_type:shader\"]]", CancellationToken.None).ConfigureAwait(true);
                    break;
                case Core.Net.ModrinthCategory.ResourcePacks:
                    hits = await Core.Net.Modrinth.SearchByFacetsAsync(
                        _searchText.Trim(), "[[\"project_type:resourcepack\"]]", CancellationToken.None).ConfigureAwait(true);
                    break;
                default:
                    hits = await Core.Net.Modrinth.SearchAsync(
                        _searchText.Trim(), inst.InstanceId, "fabric", CancellationToken.None).ConfigureAwait(true);
                    break;
            }

            HashSet<string>? installedNames = null;
            if (_category == Core.Net.ModrinthCategory.Mods)
            {
                installedNames = Core.Instances.InstanceRegistry.ListMods(inst.InstanceId)
                    .Select(m => m.BaseName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            Rows.Clear();
            foreach (var hit in hits)
            {
                var installed = installedNames?.Contains(hit.Title.ToLowerInvariant()) == true;
                Rows.Add(new ModrinthHitVM(hit, h => _ = Install(h), installed));
            }

            var scopeText = _category switch
            {
                Core.Net.ModrinthCategory.Shaders => $"{inst.InstanceId} · 光影通用版",
                Core.Net.ModrinthCategory.ResourcePacks => $"{inst.InstanceId} · 资源包通用版",
                _ => $"{inst.InstanceId} · Fabric",
            };
            StatusText = Rows.Count == 0
                ? "没有匹配的结果(换个关键词试试)。"
                : $"共 {Rows.Count} 条结果({scopeText})。";
        }
        catch (Exception ex)
        {
            Rows.Clear();
            StatusText = "检索失败:" + ex.Message;
            AppServices.Notify.Publish(NotifyKind.Error, "Modrinth 检索失败:" + ex.Message);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>安装:解析该项目最新 primary 文件 → 提交真实传输(SHA-1 校验)到对应目录(mods / shaderpacks / resourcepacks)。</summary>
    private async Task Install(ModrinthHitVM hit)
    {
        var inst = Core.Instances.InstanceRegistry.Latest();
        if (inst is null)
            return;

        try
        {
            // 光影 / 资源包不挂 game_versions 过滤(通用兼容);模组按实例版本 + Fabric 过滤
            var vf = _category == Core.Net.ModrinthCategory.Mods
                ? await Core.Net.Modrinth.LatestVersionFileAsync(
                    hit.Slug, inst.InstanceId, "fabric", CancellationToken.None).ConfigureAwait(true)
                : await Core.Net.Modrinth.LatestVersionFileAsync(hit.Slug, CancellationToken.None).ConfigureAwait(true);

            var destDir = _category switch
            {
                Core.Net.ModrinthCategory.Shaders => Path.Combine(
                    Core.Instances.InstanceRegistry.InstanceDirOf(inst.InstanceId), "shaderpacks"),
                Core.Net.ModrinthCategory.ResourcePacks => Path.Combine(
                    Core.Instances.InstanceRegistry.InstanceDirOf(inst.InstanceId), "resourcepacks"),
                _ => Core.Instances.InstanceRegistry.ModsDirOf(inst.InstanceId),
            };
            Directory.CreateDirectory(destDir);

            if (vf is null)
            {
                await AppServices.Dialogs.AlertAsync("无法安装",
                    $"「{hit.Title}」没有可用的文件(模组可能未适配 {inst.InstanceId}/Fabric)。\n请在 Modrinth 网站选择对应版本。", MsgMark.Warning);
                return;
            }

            var dest = Path.Combine(destDir, vf.FileName);
            AppServices.Transfers.Submit(new TransferSpec
            {
                DisplayName = $"{vf.FileName}(Modrinth · {hit.Title})",
                Kind = TransferKind.Http,
                BytesTotal = vf.Size,
                ExpectedSha1 = vf.Sha1,
                DestPath = dest,
                Sources = [new TransferSourceSpec(vf.Url, isMirror: false)],
            });
            var where = _category switch
            {
                Core.Net.ModrinthCategory.Shaders => "shaderpacks",
                Core.Net.ModrinthCategory.ResourcePacks => "resourcepacks",
                _ => "mods",
            };
            AppServices.Notify.Publish(NotifyKind.Success,
                $"已加入任务中心:{vf.FileName}({vf.SizeText}),完成后在 {where} 目录生效");
        }
        catch (Exception ex)
        {
            AppServices.Notify.Publish(NotifyKind.Error, "解析 Modrinth 文件失败:" + ex.Message);
        }
    }
}


// ======================================================== 设置

public sealed class SettingsPageVM : VmBase
{
    public SettingsPageVM()
    {
        // 系统深浅色由 ThemeManager 监听并刷新，这里同步提示文本
        Themes.ThemeManager.ThemeChanged += () => On(nameof(SystemHint));
    }

    public string ThemeMode
    {
        get => AppServices.Settings.Current.Theme switch
        {
            Core.Settings.ThemePref.Light => "light",
            Core.Settings.ThemePref.FollowSystem => "system",
            _ => "dark",
        };
        set
        {
            var pref = value switch
            {
                "light" => Core.Settings.ThemePref.Light,
                "system" => Core.Settings.ThemePref.FollowSystem,
                _ => Core.Settings.ThemePref.Dark,
            };
            AppServices.Settings.Update(cfg => cfg.Theme = pref);
            ApplyTheme();
            On(nameof(ThemeMode));
            On(nameof(SystemHint));
        }
    }

    /// <summary>配色方案（salmon / ink / sand / slate）</summary>
    public string PaletteId
    {
        get => AppServices.Settings.Current.Palette;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == PaletteId)
                return;
            AppServices.Settings.Update(cfg => cfg.Palette = value);
            ApplyTheme();
            On(nameof(PaletteId));
        }
    }

    /// <summary>强调色（coral / ocean / forest / violet / amber）</summary>
    public string AccentId
    {
        get => AppServices.Settings.Current.Accent;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == AccentId)
                return;
            AppServices.Settings.Update(cfg => cfg.Accent = value);
            ApplyTheme();
            On(nameof(AccentId));
        }
    }

    private static void ApplyTheme()
    {
        var cfg = AppServices.Settings.Current;
        Themes.ThemeManager.Apply(cfg.Theme, cfg.Palette, cfg.Accent);
    }

    public int Concurrency
    {
        get => AppServices.Transfers.Options.MaxConcurrency;
        set
        {
            var clamped = Math.Clamp(value, 2, 16);
            AppServices.Settings.Update(cfg => cfg.TransferConcurrency = clamped);
            On(nameof(ConcurrencyNote));
        }
    }

    public string ConcurrencyNote => AppServices.TaskCenter.ConcurrencyNote;

    public string SystemHint => $"系统深色: {(Themes.ThemeManager.IsOsDark() ? "开" : "关")}";
}

// ======================================================== 皮肤站(阶段五 · 本地真实)

public sealed class SkinStationVM : VmBase
{
    public string AccountText
    {
        get
        {
            var type = AppServices.Settings.Current.AccountType;
            if (type == "msa")
            {
                var s = Core.Account.MsAuth.LoadSession();
                if (s is not null && s.MinecraftValid)
                    return "微软正版 · " + s.Name;
                return "微软正版 · 会话已过期,点「微软正版登录」续签";
            }
            return "离线账户 · " + AppServices.Settings.Current.AccountName;
        }
    }

    public string MsaHint
    {
        get
        {
            if (AppServices.Settings.Current.AccountType != "msa")
                return "当前离线启动:登录微软后自动切换到正版身份(需已购买 Java 版)。皮肤将直接读取官方档案。";
            var s = Core.Account.MsAuth.LoadSession();
            return s is null
                ? "尚未完成微软登录:点「微软正版登录」,在浏览器完成授权后把回调链接粘贴回来即可。"
                : $"正版档案 {s.Name}({s.Uuid}) · 启动时自动续签令牌。";
        }
    }

    public string HintText { get; private set; } = "";

    public bool HasSkin { get; private set; }

    public BitmapSource? SkinFull { get; private set; }
    public BitmapSource? SkinHead { get; private set; }

    private string SkinPath => System.IO.Path.Combine(
        Core.Boot.Runtime.Root, "accounts", DisplayName(), "skin.png");

    /// <summary>皮肤目录跟随当前身份:正版用档案名,离线用离线名。</summary>
    private static string DisplayName()
    {
        if (AppServices.Settings.Current.AccountType == "msa")
        {
            var s = Core.Account.MsAuth.LoadSession();
            if (s is not null && s.MinecraftValid && s.Name.Length > 0)
                return s.Name;
        }
        return AppServices.Settings.Current.AccountName;
    }

    public ICommand ImportCommand { get; }
    public ICommand ChangeAccountCommand { get; }
    public ICommand OpenDirCommand { get; }
    public ICommand MsaLoginCommand { get; }

    public string MsaButtonText =>
        AppServices.Settings.Current.AccountType == "msa" ? "退出微软正版" : "微软正版登录";

    /// <summary>登录/登出中(防止重复发起)。</summary>
    private bool _msaBusy;

    public SkinStationVM()
    {
        ImportCommand = new RelayCommand(() => ImportSkin());
        ChangeAccountCommand = new RelayCommand(() => _ = ChangeAccountAsync());
        OpenDirCommand = new RelayCommand(() =>
        {
            var dir = System.IO.Path.GetDirectoryName(SkinPath);
            if (!string.IsNullOrEmpty(dir))
                OpenHelpers.OpenExplorer(dir);
        });
        MsaLoginCommand = new RelayCommand(() => _ = ToggleMsaAsync(), () => !_msaBusy);
        RefreshPreview();
        AppServices.Settings.Changed += _ =>
        {
            On(nameof(AccountText));
            On(nameof(MsaHint));
            On(nameof(MsaButtonText));
            RefreshPreview();
        };
    }

    /// <summary>登录(浏览器 PKCE 授权)或退出登录(清会话并回到离线)。</summary>
    private async Task ToggleMsaAsync()
    {
        var mode = AppServices.Settings.Current.AccountType;
        try
        {
            if (mode == "msa")
            {
                var ok = await AppServices.Dialogs.ConfirmAsync("退出微软正版",
                    "将清除本机保存的微软会话并回到离线启动模式。\n继续?", MsgMark.Warning, okLabel: "退出登录");
                if (!ok)
                    return;
                Core.Account.MsAuth.ClearSession();
                AppServices.Settings.Update(cfg => cfg.AccountType = "offline");
                AppServices.Notify.Publish(NotifyKind.Success, "已退出微软正版,回到离线账户模式");
            }
            else
            {
                _msaBusy = true;
                await LoginMsaAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            AppServices.Notify.Publish(NotifyKind.Error, "微软登录失败:" + ex.Message);
            Core.Logging.AppLog.Warn("MSLogin", ex.Message);
        }
        finally
        {
            _msaBusy = false;
            On(nameof(AccountText));
            On(nameof(MsaHint));
            RefreshPreview();
        }
    }

    /// <summary>微软正版登录:浏览器 PKCE 授权 → 粘贴回调链接 → 换取正版会话。</summary>
    private async Task LoginMsaAsync()
    {
        var request = Core.Account.MsAuth.BeginInteractive();
        OpenHelpers.OpenUri(request.Url); // 打开默认浏览器

        var input = await AppServices.Dialogs.PromptAsync(
            "微软正版登录",
            "1. 已在浏览器打开微软登录页,请完成账户登录(需已购买 Java 版)。\n"
            + "2. 登录后浏览器会跳转到一个空白页;复制地址栏里的完整链接。\n"
            + "3. 把链接粘贴到下面并确定,启动器会自动换取正版会话。",
            "");

        if (string.IsNullOrWhiteSpace(input))
        {
            AppServices.Notify.Publish(NotifyKind.Info, "已取消微软正版登录");
            return;
        }

        var code = Core.Account.MsAuth.ExtractAuthorizationCode(input);
        var session = await Core.Account.MsAuth
            .CompleteInteractiveAsync(code, request.Verifier,
                step => AppServices.Notify.Publish(NotifyKind.Info, "正版:" + step))
            .ConfigureAwait(true);

        Core.Account.MsAuth.SaveSession(session);
        AppServices.Settings.Update(cfg =>
        {
            cfg.AccountType = "msa";
            if (session.Name.Length > 0)
                cfg.AccountName = session.Name;
        });
        AppServices.Notify.Publish(NotifyKind.Success, $"微软正版登录成功:{session.Name}");
    }

    private void RefreshPreview()
    {
        var path = SkinPath;
        try
        {
            if (!File.Exists(path))
            {
                HasSkin = false;
                SkinFull = null;
                SkinHead = null;
                HintText = $"尚未为「{AppServices.Settings.Current.AccountName}」导入皮肤:点「导入皮肤(.png)」选择一张官方 64×64 / 64×32 皮肤图。";
            }
            else
            {
                var img = new BitmapImage();
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                {
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.StreamSource = fs;
                    img.EndInit();
                }
                img.Freeze();

                var head = new CroppedBitmap(img, new Int32Rect(8, 8, 8, 8));
                var body = new CroppedBitmap(img, new Int32Rect(20, 20, 8, 12));
                head.Freeze();
                body.Freeze();

                SkinFull = img;
                SkinHead = head;
                HasSkin = true;
                HintText = $"皮肤已就绪({img.PixelWidth}×{img.PixelHeight}),存放于 accounts\\{AppServices.Settings.Current.AccountName}\\skin.png;离线模式下由客户端侧模组加载(如 CustomSkinLoader)。";
            }
        }
        catch (Exception ex)
        {
            HasSkin = false;
            HintText = "皮肤预览失败:" + ex.Message;
        }

        On(nameof(HasSkin));
        On(nameof(SkinFull));
        On(nameof(SkinHead));
        On(nameof(HintText));
    }

    private void ImportSkin()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入皮肤",
            Filter = "皮肤图像|*.png|任何文件|*.*",
        };
        if (dlg.ShowDialog() is not true)
            return;

        try
        {
            var dir = System.IO.Path.GetDirectoryName(SkinPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.Copy(dlg.FileName, SkinPath, overwrite: true);
            AppServices.Notify.Publish(NotifyKind.Success, "皮肤已导入(本地缓存,不影响原文件)");
        }
        catch (Exception ex)
        {
            AppServices.Notify.Publish(NotifyKind.Error, "导入失败:" + ex.Message);
        }
        RefreshPreview();
    }

    private async Task ChangeAccountAsync()
    {
        var name = await AppServices.Dialogs.PromptAsync("切换账户",
            "输入游戏内玩家名(离线模式;UUID 由名字推导,最多 16 字符):",
            AppServices.Settings.Current.AccountName).ConfigureAwait(true);
        if (name is null)
            return;

        name = name.Trim();
        if (name.Length is < 3 or > 16 || name.Any(c => !"abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".Contains(c)))
        {
            await AppServices.Dialogs.AlertAsync("无效的名字",
                "离线玩家名限制:3-16 字符,仅字母 / 数字 / 下划线。", MsgMark.Warning);
            return;
        }

        AppServices.Settings.Update(c => c.AccountName = name);
        On(nameof(AccountText));
        RefreshPreview();
        AppServices.Notify.Publish(NotifyKind.Success, $"账户已切换:{name}(下次启动生效)");
    }
}

// ======================================================== 崩溃诊断(阶段六 · 真实解析)

public sealed class CrashReportRowVM : VmBase
{
    public string Path { get; }
    public DateTimeOffset Time { get; }
    public string Display { get; }

    public CrashReportRowVM(string path, DateTimeOffset time)
    {
        Path = path;
        Time = time;
        var name = System.IO.Path.GetFileName(path);
        var inst = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(path)));
        Display = $"[{time:MM-dd HH:mm}] {inst} · {name}";
    }
}

public sealed class CrashDiagVM : VmBase
{
    private CrashReportRowVM? _selected;
    private string _excerptText = "选择左侧报告后显示诊断。";

    public ObservableCollection<CrashReportRowVM> Reports { get; } = [];
    public ObservableCollection<string> Findings { get; } = [];

    public string ExcerptText { get => _excerptText; private set => Set(ref _excerptText, value); }

    public CrashReportRowVM? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value) && value is not null)
                Analyze(value.Path);
        }
    }

    public ICommand RefreshCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand OpenDirCommand { get; }

    public CrashDiagVM()
    {
        RefreshCommand = new RelayCommand(Refresh);
        OpenFileCommand = new RelayCommand(() =>
        {
            if (_selected is not null)
                OpenHelpers.OpenExplorer(System.IO.Path.GetDirectoryName(_selected.Path)!);
        });
        OpenDirCommand = new RelayCommand(() => OpenHelpers.OpenExplorer(Core.Boot.Runtime.InstancesDir));
        Refresh();
    }

    /// <summary>再入本页:重扫报告并保持/恢复最佳选择。</summary>
    public void RefreshData() => Refresh();

    private void Refresh()
    {
        Reports.Clear();
        foreach (var (path, time) in Core.Tools.CrashDiagnostics.ListReadable())
            Reports.Add(new CrashReportRowVM(path, time));

        if (_selected is null)
        {
            Selected = Reports.FirstOrDefault();
        }
        else
        {
            // 保持选择不变;若该文件已消失则回到最新
            var still = Reports.FirstOrDefault(r => r.Path == _selected.Path);
            Selected = still ?? Reports.FirstOrDefault();
        }
    }

    private void Analyze(string path)
    {
        var diag = Core.Tools.CrashDiagnostics.Analyze(path);
        Findings.Clear();
        Findings.Add($"诊断目标:{diag.File}");
        if (diag.Findings.Count == 0)
            Findings.Add("未命中已知错误签名;请阅读下方正文(或把报告给到社区)以继续定位。");
        else
            foreach (var f in diag.Findings)
                Findings.Add(f);
        ExcerptText = string.IsNullOrWhiteSpace(diag.Excerpt) ? "(无正文)" : diag.Excerpt;
        On(nameof(Selected));
    }
}

// ======================================================== 应用日志(阶段六 · 真实读取)

public sealed class AppLogVM : VmBase, Infrastructure.IRefreshable
{
    private readonly bool _instanceMode;
    private string _filter = "";
    private string _statusText = "";

    public ObservableCollection<string> Lines { get; } = [];

    public string FilterText { get => _filter; set => Set(ref _filter, value); }

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    public ICommand RefreshCommand { get; }
    public ICommand OpenDirCommand { get; }

    /// <param name="instanceMode">true = 显示最近实例的运行日志(latest.log);false = 启动器自身日志。</param>
    public AppLogVM(bool instanceMode = false)    {
        _instanceMode = instanceMode;
        RefreshCommand = new RelayCommand(Refresh);
        OpenDirCommand = new RelayCommand(() =>
        {
            if (_instanceMode)
            {
                var inst = Core.Instances.InstanceRegistry.Latest();
                if (inst is not null)
                    OpenHelpers.OpenExplorer(System.IO.Path.Combine(
                        Core.Boot.Runtime.InstancesDir, inst.InstanceId, "logs"));
            }
            else
            {
                OpenHelpers.OpenExplorer(Core.Boot.Runtime.LogDir);
            }
        });
        Refresh();
    }

    public void Refresh()
    {
        Lines.Clear();
        string? file;
        if (_instanceMode)
        {
            var inst = Core.Instances.InstanceRegistry.Latest();
            file = inst is null ? null : Core.Tools.LogReader.LatestInstanceLog(inst.InstanceId);
            if (file is null)
            {
                StatusText = "该实例还没有运行日志(先启动一次游戏)。";
                return;
            }
        }
        else
        {
            file = Core.Tools.LogReader.LauncherLogs().FirstOrDefault();
            if (file is null)
            {
                StatusText = "尚无应用日志(启动器首次运行)。";
                return;
            }
        }

        var tail = Core.Tools.LogReader.ReadTail(file, 400, _filter);
        if (tail is null)
        {
            StatusText = "日志读取失败(文件被占用)。";
            return;
        }

        foreach (var line in tail.Lines)
            Lines.Add(line);
        StatusText = $"{System.IO.Path.GetFileName(tail.File)} · 尾部 {tail.Lines.Count} 行 · WARN {tail.WarnCount} / ERROR {tail.ErrorCount}"
                     + (string.IsNullOrWhiteSpace(_filter) ? "" : $" · 过滤:「{_filter}」");
    }

    /// <summary>再入本页:重读日志尾部(游戏退出后 latest.log 内容已更新)。</summary>
    public void RefreshData() => Refresh();
}

// ======================================================== VoxLink(阶段六 · 本地信令真实通路)

public sealed class VoxLinkVM : VmBase
{
    private Core.Net.VoxLinkHost? _host;
    private Core.Net.VoxLinkGuest? _guest;
    private string _statusText = "创建房间或输入邀请码加入(本机回环信令;公网穿透需中继,后续接入)。";
    private string _inviteText = "";
    private string _input = "大家好,这是一条 VoxLink 消息。";

    public ObservableCollection<string> Members { get; } = [];
    public ObservableCollection<string> Messages { get; } = [];

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string InviteText { get => _inviteText; private set => Set(ref _inviteText, value); }
    public string InputText { get => _input; set => Set(ref _input, value); }

    public ICommand HostCommand { get; }
    public ICommand JoinCommand { get; }
    public ICommand SendCommand { get; }

    public VoxLinkVM()
    {
        HostCommand = new RelayCommand(() => _ = HostAsync());
        JoinCommand = new RelayCommand(() => _ = JoinAsync());
        SendCommand = new RelayCommand(() => _ = SendAsync());
        Members.Add("(尚未创建/加入房间)");
    }

    private async Task HostAsync()
    {
        try
        {
            if (_host?.Active is true)
            {
                StatusText = "房间已在运行;邀请码见下方。";
                return;
            }

            var name = AppServices.Settings.Current.AccountName;
            _host?.Dispose();
            _host = new Core.Net.VoxLinkHost();
            _host.Joined += m => OnUi(() =>
            {
                Members.Remove("(尚未创建/加入房间)");
                Members.Add($"{m.Name}(成员)");
            });
            _host.Message += m => OnUi(() => Messages.Insert(0, m));
            _host.Start(name + " 的房间");

            InviteText = _host.InviteCode;
            Members.Clear();
            Members.Add($"{name}(房主,localhost:{_host.Port})");
            Messages.Insert(0, $"房间已创建 · localhost:{_host.Port} · 邀请码 {InviteText}");
            StatusText = $"房间运行中(localhost:{_host.Port});邀请码已生成,可本机演示加入。";
        }
        catch (Exception ex)
        {
            StatusText = "创建房间失败:" + ex.Message;
        }
        await Task.CompletedTask;
    }

    private async Task JoinAsync()
    {
        var code = await AppServices.Dialogs.PromptAsync("加入 VoxLink 房间",
            "粘贴房主给的邀请码(SLROOM-xxxx-xx 形式):", InviteText).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(code))
            return;

        _guest?.Dispose();
        _guest = new Core.Net.VoxLinkGuest();
        _guest.Message += m => OnUi(() => Messages.Insert(0, m));
        try
        {
            await _guest.ConnectAsync(code.Trim(), AppServices.Settings.Current.AccountName).ConfigureAwait(true);
            StatusText = "已连接到房间(本地信令);下方可发言/发 LAN Ping。";
            Members.Clear();
            Members.Add(AppServices.Settings.Current.AccountName + "(我)");
        }
        catch (Exception ex)
        {
            StatusText = "加入失败:" + ex.Message;
        }
    }

    private async Task SendAsync()
    {
        if (_guest is not null)
        {
            _guest.Send(InputText);
            Messages.Insert(0, $"[我] {InputText}");
            return;
        }
        if (_host is { Active: true })
        {
            _host.Broadcast($"[房主] {InputText}");
            Messages.Insert(0, $"[我] 已广播 {InputText}");
            return;
        }
        await AppServices.Dialogs.AlertAsync("未联机", "先创建房间或加入一个房间再发送。", MsgMark.Warning);
    }

    public string MembersCount => $"成员 {Members.Count}";

    private static void OnUi(Action action)
    {
        var d = System.Windows.Application.Current?.Dispatcher;
        if (d is null || d.CheckAccess())
            action();
        else
            d.Invoke(action);
    }
}

// ======================================================== 存档管理(阶段四收尾 · 真实)

public sealed class SaveRowVM
{
    public string Name { get; }
    public string Meta { get; }

    public SaveRowVM(Core.Instances.InstanceRegistry.SaveInfo save)
    {
        Name = save.Name;
        Meta = $"{save.SizeMb} MB · 最近写入 {save.LastPlayed:MM-dd HH:mm}";
    }
}

public sealed class InstanceSavesVM : VmBase, Infrastructure.IRefreshable
{
    private string _instance;
    private string _savesDir;
    private string _backupsDir;

    public ObservableCollection<SaveRowVM> Rows { get; } = [];

    public string SubTitle { get; private set; } = "";
    public ICommand BackupCommand { get; }
    public ICommand OpenSavesCommand { get; }
    public ICommand OpenBackupsCommand { get; }

    public InstanceSavesVM()
    {
        _instance = "";
        _savesDir = "";
        _backupsDir = "";

        BackupCommand = new RelayCommand(() =>
        {
            try
            {
                var backup = Core.Instances.InstanceRegistry.BackupSaves(_instance);
                if (backup is null)
                    AppServices.Notify.Publish(NotifyKind.Warning, "saves 目录为空,没有可备份的存档");
                else
                    AppServices.Notify.Publish(NotifyKind.Success, $"已备份 → {System.IO.Path.GetFileName(backup)}");
            }
            catch (Exception ex)
            {
                AppServices.Notify.Publish(NotifyKind.Error, "备份失败:" + ex.Message);
            }
        });
        OpenSavesCommand = new RelayCommand(() => { if (_savesDir.Length > 0) OpenHelpers.OpenExplorer(_savesDir); });
        OpenBackupsCommand = new RelayCommand(() => { if (_backupsDir.Length > 0) OpenHelpers.OpenExplorer(_backupsDir); });

        RefreshData();
    }

    /// <summary>再入本页:重解析实例并重扫 saves。</summary>
    public void RefreshData()
    {
        _instance = Core.Launch.LaunchPipeline.LatestInstanceId() ?? "";
        _savesDir = System.IO.Path.Combine(Core.Instances.InstanceRegistry.InstanceDirOf(_instance), "saves");
        _backupsDir = System.IO.Path.Combine(Core.Instances.InstanceRegistry.InstanceDirOf(_instance), "backups");
        SubTitle = _instance.Length == 0
            ? "尚未安装版本;安装后本页按实例展示存档。"
            : $"实例 {_instance} · 存档与备份均为磁盘真实文件。";
        On(nameof(SubTitle));

        Rows.Clear();
        if (_instance.Length == 0)
            return;
        foreach (var s in Core.Instances.InstanceRegistry.ListSaves(_instance))
            Rows.Add(new SaveRowVM(s));
    }
}
