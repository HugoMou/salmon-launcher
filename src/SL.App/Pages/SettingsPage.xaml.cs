using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using SL.App.Account;
using SL.App.Infrastructure;
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

        TermsStateText.Text = c.TermsAccepted
            ? "已阅读并同意"
            : "尚未同意 · 点击右侧按钮查看并确认";

        _loading = false;
    }

    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        var pref = ReferenceEquals(sender, ModeLight) ? SL.Core.Settings.ThemePref.Light
            : ReferenceEquals(sender, ModeDark) ? SL.Core.Settings.ThemePref.Dark
            : SL.Core.Settings.ThemePref.FollowSystem;
        AppServices.Settings.Update(c => c.Theme = pref);
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
    }

    private void OnConcurrencyChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue))
            return;
        AppServices.Settings.Update(c => c.TransferConcurrency = (int)Math.Clamp(args.NewValue, 2, 16));
    }

    private void OnOpenWorkshop(object sender, RoutedEventArgs e) => AppServices.Nav.Navigate("st.theme");

    private void OnOpenTerms(object sender, RoutedEventArgs e) => AppServices.Nav.Navigate("st.terms");
}
