using System.Windows;
using System.Windows.Input;
using SL.Pres.Controls;
using SL.Pres.Infrastructure;
using SL.Pres.Notify;

namespace SL.Pres.Navigation;

/// <summary>
/// 双栏主从导航模型:
/// - 主区(zone)= 标题栏四个标签;子页(PageCurrentSub)= 分区内活动页;
/// - 下钻(Open 到更深层级)把当前页压入栈;返回从栈弹出并恢复原来的左侧面板状态;
/// - 同层级之间(例如左侧导航切换)为平移替换,不污染返回栈;
/// - 每个分区独立维护「当前页 + 栈」,切换顶级标签时整体替换左右面板;
/// - 过渡动画方向由状态机统一提供,页面不持有动画逻辑。
/// </summary>
public sealed class NavVM : VmBase
{
    public sealed record NavItem(string Key, string Label, string Glyph);

    public sealed record NavGroup(string Title, IReadOnlyList<NavItem> Items);

    /// <summary>单一左导航的静态结构（WinUI NavigationView 风格）。</summary>
    public IReadOnlyList<NavGroup> Groups { get; } =
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

    private static readonly Dictionary<NavZone, string> ZoneRoots = new()
    {
        [NavZone.Launch] = "launch.home",
        [NavZone.Download] = "dl.game",
        [NavZone.Settings] = "st.launch",
        [NavZone.Tools] = "tools.online",
    };

    public static string ZoneLabel(NavZone zone) => zone switch
    {
        NavZone.Launch => "启动",
        NavZone.Download => "下载",
        NavZone.Settings => "设置",
        _ => "工具",
    };

    private sealed class ZoneState
    {
        public string Page = "";
        public readonly List<string> Stack = [];
    }

    private NavZone _zone = NavZone.Launch;
    private string _current = "";
    private readonly Dictionary<NavZone, ZoneState> _states = new();
    private bool _leftCollapsed;

    /// <summary>侧栏折叠开关(标题栏按钮触发,布局动画由 MainWindow 执行)</summary>
    public event Action? SidebarToggleRequested;

    public UIElement? ContentView { get; private set; }
    public UIElement? LeftView { get; private set; }
    public TransitionKind Direction { get; private set; } = TransitionKind.Forward;
    public string PathText { get; private set; } = "";
    public string PageTitle { get; private set; } = "";
    public string CurrentKey { get; private set; } = "";
    public bool CanBack { get; private set; }

    public bool IsLeftCollapsed
    {
        get => _leftCollapsed;
        private set
        {
            if (Set(ref _leftCollapsed, value))
            {
                On(nameof(SideToggleGlyph));
                On(nameof(ShowLabels));
            }
        }
    }

    public string SideToggleGlyph => _leftCollapsed ? "»" : "«";

    /// <summary>左导航展开时显示文字标签</summary>
    public bool ShowLabels => !_leftCollapsed;

    /// <summary>当前分区(供标题栏标签的选中态绑定)</summary>
    public NavZone Zone { get; private set; }

    public NavVM()
    {
        foreach (var zone in Enum.GetValues<NavZone>())
            _states[zone] = new ZoneState { Page = ZoneRoots[zone] };

        _current = ZoneRoots[NavZone.Launch];
        Zone = NavZone.Launch;
        Refresh(); // Launch 面板在启动时即构建,确保立即可用
    }

    public void SwitchZone(NavZone zone)
    {
        if (zone == _zone)
            return;

        _states[_zone].Page = _current;
        _zone = zone;
        Zone = zone;
        _current = _states[zone].Page;
        SetDirection(TransitionKind.Forward);
        Refresh();
        On(nameof(Zone));
    }

    /// <summary>左导航点击：按页面归属自动切换分区并平铺切换（不污染返回栈）。</summary>
    public void Navigate(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;

        if (!PageCatalog.All.ContainsKey(key))
        {
            AppServices.Notify.Publish(NotifyKind.Error, $"未注册页面:{key}");
            return;
        }

        var zone = ZoneOf(key);
        if (zone != _zone)
        {
            _states[_zone].Page = _current;
            _zone = zone;
            Zone = zone;
            On(nameof(Zone));
        }

        var state = _states[zone];
        if (state.Page == key && _current == key)
            return;

        state.Page = key;
        state.Stack.Clear();
        _current = key;
        SetDirection(TransitionKind.Forward);
        Refresh();
    }

    private static NavZone ZoneOf(string key)
    {
        if (key.StartsWith("dl.", StringComparison.Ordinal))
            return NavZone.Download;
        if (key.StartsWith("st.", StringComparison.Ordinal))
            return NavZone.Settings;
        if (key.StartsWith("tools.", StringComparison.Ordinal))
            return NavZone.Tools;
        return NavZone.Launch;
    }

    /// <summary>按页面层级语义导航:更深 = 下钻入栈(Forward),更浅 = 收拢栈(Back),同层 = 替换栈顶。</summary>
    public void Open(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key == _current)
            return;

        if (!System.Linq.Enumerable.Any(PageCatalog.All, kv => kv.Key == key))
        {
            AppServices.Notify.Publish(NotifyKind.Error, $"未注册页面:{key}");
            return;
        }

        var target = PageCatalog.Get(key);
        var host = PageCatalog.Get(_current);
        var state = _states[_zone];
        var dir = TransitionKind.Forward;

        if (target.Level > host.Level)
        {
            state.Stack.Add(_current);
        }
        else if (target.Level < host.Level)
        {
            // 跨层向上:退回目标层级
            while (state.Stack.Count > target.Level)
                state.Stack.RemoveAt(state.Stack.Count - 1);
            dir = TransitionKind.Back;
        }
        else if (state.Stack.Count > 0)
        {
            // 同层平移:替换栈顶(记录上一页)
            state.Stack[^1] = _current;
        }

        _states[_zone].Page = key;
        _current = key;
        SetDirection(dir);
        Refresh();
    }

    public void Back()
    {
        var state = _states[_zone];
        if (state.Stack.Count == 0)
            return;

        _current = state.Stack[^1];
        state.Stack.RemoveAt(state.Stack.Count - 1);
        state.Page = _current;
        SetDirection(TransitionKind.Back);
        Refresh();
    }

    public void ToggleSideCollapsed()
    {
        IsLeftCollapsed = !_leftCollapsed;
        SidebarToggleRequested?.Invoke();
    }

    // ------------------------------------------------------------ 渲染同步

    private void Refresh()
    {
        var spec = PageCatalog.Get(_current);
        ContentView = PageCatalog.ContentFor(_current);
        LeftView = PageCatalog.LeftFor(_current);

        var state = _states[_zone];
        var chain = new List<string>();
        chain.AddRange(state.Stack.Where(k => k != ZoneRoots[_zone]));
        if (_current != ZoneRoots[_zone])
            chain.Add(_current);

        var parts = new List<string> { ZoneLabel(_zone) };
        parts.AddRange(chain.Select(k => PageCatalog.Get(k).Title));

        PathText = string.Join(" / ", parts);
        PageTitle = spec.Title;
        CurrentKey = _current;
        CanBack = state.Stack.Count > 0;

        On(nameof(ContentView));
        On(nameof(LeftView));
        On(nameof(PathText));
        On(nameof(PageTitle));
        On(nameof(CurrentKey));
        On(nameof(CanBack));
    }

    private void SetDirection(TransitionKind kind)
    {
        Direction = kind;
        On(nameof(Direction));
    }

    // ------------------------------------------------------------ 命令

    public ICommand NavigateCommand { get; } = new RelayCommand<string>(
        key => AppServices.Nav.Navigate(key ?? ""),
        key => !string.IsNullOrWhiteSpace(key));

    public ICommand SwitchZoneCommand { get; } = new RelayCommand<string>(s =>
    {
        if (int.TryParse(s, out var index) && index >= 0 && index <= 3)
            AppServices.Nav.SwitchZone((NavZone)index);
    });

    public ICommand BackCommand { get; } = new RelayCommand(
        () => AppServices.Nav.Back(),
        () => AppServices.Nav is not null && AppServices.Nav.CanBack);

    public ICommand ToggleSideCommand { get; } = new RelayCommand(
        () => AppServices.Nav.ToggleSideCollapsed());
}
