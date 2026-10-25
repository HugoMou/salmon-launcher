using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SL.App.Account;
using SL.App.Dialogs;
using SL.App.Infrastructure;
using SL.App.Localization;
using SL.App.Themes;

namespace SL.App.Pages;

/// <summary>配色方案卡片。</summary>
public sealed class PaletteCardVM
{
    public PaletteCardVM(ThemePalette palette, string currentId)
    {
        Palette = palette;
        var colors = ThemeManager.PreviewColors(palette);
        Swatch1 = Solid(colors[0]);
        Swatch2 = Solid(colors[1]);
        Swatch3 = Solid(colors[2]);
        CheckVisibility = palette.Id == currentId ? Visibility.Visible : Visibility.Collapsed;
    }

    public ThemePalette Palette { get; }

    public string Name => Palette.Name;

    public Brush Swatch1 { get; }

    public Brush Swatch2 { get; }

    public Brush Swatch3 { get; }

    public Visibility CheckVisibility { get; }

    private static Brush Solid(string hex) => new SolidColorBrush(ThemeManager.FromHex(hex));
}

/// <summary>强调色圆点。</summary>
public sealed class AccentCardVM
{
    public AccentCardVM(AccentChoice accent, string currentId)
    {
        Accent = accent;
        Fill = new SolidColorBrush(ThemeManager.FromHex(accent.Dark));
        RingVisibility = accent.Id == currentId ? Visibility.Visible : Visibility.Collapsed;
    }

    public AccentChoice Accent { get; }

    public string Name => Accent.Name;

    public Brush Fill { get; }

    public Visibility RingVisibility { get; }
}

/// <summary>外观与设置:账户、主题(深浅/配色/强调)、下载并发与协议状态。</summary>
public sealed partial class SettingsPage : Page
{
    private readonly AccountVM _account = new();
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
        AccountRoot.DataContext = _account;
        LoadConfig();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadConfig();
    }

    private void LoadConfig()
    {
        _loading = true;
        var c = AppServices.Settings.Current;

        ModeSystem.IsChecked = c.Theme == SL.Core.Settings.ThemePref.FollowSystem;
        ModeLight.IsChecked = c.Theme == SL.Core.Settings.ThemePref.Light;
        ModeDark.IsChecked = c.Theme == SL.Core.Settings.ThemePref.Dark;

        PaletteGrid.ItemsSource = ThemeManager.Palettes
            .Select(p => new PaletteCardVM(p, c.Palette))
            .ToList();

        AccentList.ItemsSource = ThemeManager.Accents
            .Select(a => new AccentCardVM(a, c.Accent))
            .ToList();

        ConcurrencyBox.Value = c.TransferConcurrency;
        MirrorToggle.IsOn = c.ModrinthMirror;

        LanguageBox.ItemsSource = Loc.Languages
            .Select(l => l.Code == Loc.AutoCode
                ? $"{l.Name} ({Loc.DisplayName(Loc.DetectSystem())})"
                : l.Name)
            .ToList();
        var langIndex = 0;
        for (var i = 0; i < Loc.Languages.Count; i++)
        {
            if (Loc.Languages[i].Code == c.Language)
            {
                langIndex = i;
                break;
            }
        }
        LanguageBox.SelectedIndex = langIndex;

        TermsStateText.Text = c.TermsAccepted
            ? "已阅读并同意"
            : "尚未同意 · 点击右侧按钮查看并确认";

        UpdateThemeSummary(c);
        _loading = false;
    }

    /// <summary>显示当前生效的主题组合,便于确认切换结果。</summary>
    private void UpdateThemeSummary(SL.Core.Settings.AppConfig c)
    {
        var palette = ThemeManager.Palettes.FirstOrDefault(p => p.Id == c.Palette)?.Name ?? "默认";
        var accent = ThemeManager.Accents.FirstOrDefault(a => a.Id == c.Accent)?.Name ?? "默认";
        var mode = c.Theme switch
        {
            SL.Core.Settings.ThemePref.Light => "浅色",
            SL.Core.Settings.ThemePref.Dark => "深色",
            _ => "跟随系统",
        };
        ThemeCurrentText.Text = $"当前:{palette} · {accent} · {mode}";
    }

    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        var pref = ReferenceEquals(sender, ModeLight) ? SL.Core.Settings.ThemePref.Light
            : ReferenceEquals(sender, ModeDark) ? SL.Core.Settings.ThemePref.Dark
            : SL.Core.Settings.ThemePref.FollowSystem;
        AppServices.Settings.Update(c => c.Theme = pref);
        UpdateThemeSummary(AppServices.Settings.Current);
    }

    private void OnPaletteClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not PaletteCardVM card)
            return;
        ApplyPalette(card.Palette.Id);
    }

    private void OnAccentTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: AccentCardVM card })
            return;
        AppServices.Settings.Update(c => c.Accent = card.Accent.Id);
        Rebuild();
    }

    private void ApplyPalette(string paletteId)
    {
        if (AppServices.Settings.Current.Palette == paletteId)
            return;
        AppServices.Settings.Update(c => c.Palette = paletteId);
        Rebuild();
    }

    /// <summary>配色变化后刷新卡片选中态(保持列表数据与主题同步)。</summary>
    private void Rebuild()
    {
        var c = AppServices.Settings.Current;
        PaletteGrid.ItemsSource = ThemeManager.Palettes
            .Select(p => new PaletteCardVM(p, c.Palette))
            .ToList();
        AccentList.ItemsSource = ThemeManager.Accents
            .Select(a => new AccentCardVM(a, c.Accent))
            .ToList();
        UpdateThemeSummary(c);
    }

    private void OnConcurrencyChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue))
            return;
        AppServices.Settings.Update(c => c.TransferConcurrency = (int)Math.Clamp(args.NewValue, 2, 16));
    }

    private void OnOpenWorkshop(object sender, RoutedEventArgs e) => AppServices.Nav.Navigate("st.theme");
    private void OnOpenTerms(object sender, RoutedEventArgs e) => AppServices.Nav.Navigate("st.terms");

    private void OnMirrorToggled(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        AppServices.Settings.Update(c => c.ModrinthMirror = MirrorToggle.IsOn);
    }

    // ---------------------------------------------------------- 清除全部数据

    /// <summary>顺带清除的可选类目:勾选即删,不勾则按 TopEntries 保留对应顶层项。</summary>
    private sealed record ScopeOption(string Label, string Detail, string[] TopEntries);

    private static readonly ScopeOption[] Scopes =
    [
        new("实例与存档", "mods、存档、资源包、光影", ["instances"]),
        new("游戏本体与依赖", "versions、libraries、assets;之后仍可重新下载", ["versions", "libraries", "assets"]),
        new("Java 运行时", "已下载的 Temurin 等 JDK", ["java"]),
        new("下载与图片缓存", "cache 下的元数据与缩略图", ["cache"]),
    ];

    /// <summary>清除全部数据:先选择顺带清除范围,再经 10 秒冷静期确认,最后清除并退出。</summary>
    private async void OnClearAllData(object sender, RoutedEventArgs e)
    {
        // 第一步:选择除了启动器数据还顺带清除什么(不选即只清启动器数据)
        var keep = await ShowScopeAsync().ConfigureAwait(true);
        if (keep is null)
            return;

        // 第二步:10 秒冷静期,倒计时结束前无法确认
        if (!await ShowCooldownAsync(keep).ConfigureAwait(true))
            return;

        var failed = SL.Core.Boot.DataReset.Purge(keep);
        if (failed.Count > 0)
        {
            await AppServices.Dialogs.AlertAsync(
                "部分文件被占用",
                $"还有 {failed.Count} 项未能删除(通常是游戏或 Java 仍在运行)。\n这些残留会在下次启动时自动继续清除。",
                MsgMark.Warning).ConfigureAwait(true);
        }

        Application.Current.Exit();
    }

    /// <summary>第一步:勾选要顺带清除的数据;返回要保留的顶层项,null 表示取消。</summary>
    private async Task<List<string>?> ShowScopeAsync()
    {
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(new TextBlock
        {
            Text = "启动器自身的数据一定会清除:设置、账户与令牌、日志、主题、中继收藏。下面这些是可自由勾选的顺带清除项,不勾选的会保留下来:",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Opacity = 0.85,
        });

        var checks = new CheckBox[Scopes.Length];
        for (var i = 0; i < Scopes.Length; i++)
        {
            checks[i] = new CheckBox
            {
                IsChecked = true,
                Content = new StackPanel
                {
                    Spacing = 1,
                    Children =
                    {
                        new TextBlock { Text = Scopes[i].Label, FontSize = 13.5 },
                        new TextBlock { Text = Scopes[i].Detail, FontSize = 11, Opacity = 0.7 },
                    },
                },
            };
            panel.Children.Add(checks[i]);
        }

        var dialog = new ContentDialog
        {
            Title = "清除启动器数据",
            Content = panel,
            PrimaryButtonText = "下一步",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        var result = await AppServices.Dialogs.ShowCustomAsync(dialog).ConfigureAwait(true);
        if (result != ContentDialogResult.Primary)
            return null;

        var keep = new List<string>();
        for (var i = 0; i < Scopes.Length; i++)
        {
            if (checks[i].IsChecked != true)
                keep.AddRange(Scopes[i].TopEntries);
        }
        return keep;
    }

    /// <summary>第二步:10 秒冷静期;期间交代后果并锁住确认按钮,倒计时结束后才允许确认。</summary>
    private async Task<bool> ShowCooldownAsync(IReadOnlyList<string> keep)
    {
        bool Kept(ScopeOption s) => s.TopEntries.All(e => keep.Contains(e, StringComparer.OrdinalIgnoreCase));
        var kept = Scopes.Where(Kept).Select(s => s.Label).ToList();
        var removed = Scopes.Where(s => !Kept(s)).Select(s => s.Label).ToList();

        var panel = new StackPanel { Spacing = 10 };
        panel.Children.Add(new TextBlock
        {
            Text = "将删除:启动器自身的设置、账户与令牌、日志、主题、中继收藏"
                   + (removed.Count > 0 ? "、" + string.Join("、", removed) : "") + "。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
        });
        if (kept.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "将保留:" + string.Join("、", kept) + "。",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12.5,
                Opacity = 0.8,
            });
        }

        var instruction = new TextBlock
        {
            Text = "现在进入 10 秒冷静期:倒计时结束前下方按钮无法点击。10 秒之后,如果你仍然点击它,就会开始清除数据;期间随时可以点「取消」退出。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
            Opacity = 0.9,
        };
        panel.Children.Add(instruction);

        var countdown = new TextBlock
        {
            Text = "冷静期剩余 10 秒…",
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };
        panel.Children.Add(countdown);

        var dialog = new ContentDialog
        {
            Title = "10 秒冷静期",
            Content = panel,
            PrimaryButtonText = "请等待 10 秒",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        dialog.IsPrimaryButtonEnabled = false;

        var remain = 10;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            remain--;
            if (remain > 0)
            {
                countdown.Text = $"冷静期剩余 {remain} 秒…";
                return;
            }
            timer.Stop();
            countdown.Text = "冷静期结束,现在可以确认。";
            instruction.Text = "10 秒冷静期已结束:现在点击下方按钮,就会立即开始清除数据。";
            dialog.PrimaryButtonText = "仍要清除,开始";
            dialog.IsPrimaryButtonEnabled = true;
        };
        dialog.Opened += (_, _) => timer.Start();
        dialog.Closed += (_, _) => timer.Stop();

        var result = await AppServices.Dialogs.ShowCustomAsync(dialog).ConfigureAwait(true);
        return result == ContentDialogResult.Primary;
    }

    /// <summary>切换界面语言:保存设置并就地刷新整棵树(菜单/页面/提示)。</summary>
    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LanguageBox.SelectedIndex < 0 || LanguageBox.SelectedIndex >= Loc.Languages.Count)
            return;

        var code = Loc.Languages[LanguageBox.SelectedIndex].Code;
        if (code == AppServices.Settings.Current.Language)
            return;

        AppServices.Settings.Update(c => c.Language = code);
        Loc.SwitchTo(code, App.Window.Content as DependencyObject);
    }
}
