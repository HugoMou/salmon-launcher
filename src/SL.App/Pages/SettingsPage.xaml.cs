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

    /// <summary>清除全部数据:二次确认后清空数据根目录,退出应用;下次启动即为初始状态。</summary>
    private async void OnClearAllData(object sender, RoutedEventArgs e)
    {
        var confirmed = await AppServices.Dialogs.ConfirmAsync(
            "清除全部数据",
            "将删除数据目录 %APPDATA%\\SalmonLauncher 下的全部内容:实例与存档、游戏本体与依赖库、Java、账户、设置、日志、主题。\n\n此操作不可撤销,完成后启动器会自动退出,重新打开即为首次使用状态。若游戏正在运行,请先退出游戏。",
            MsgMark.Warning,
            "全部清除",
            "取消");
        if (!confirmed)
            return;

        var failed = SL.Core.Boot.DataReset.Purge();
        if (failed.Count > 0)
        {
            await AppServices.Dialogs.AlertAsync(
                "部分文件被占用",
                $"还有 {failed.Count} 项未能删除(通常是游戏或 Java 仍在运行)。\n这些残留会在下次启动时自动继续清除。",
                MsgMark.Warning);
        }

        Application.Current.Exit();
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
