using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SL.App.Account;
using SL.App.Infrastructure;

namespace SL.App.Pages;

/// <summary>外观与设置:账户(设备码正版登录)、主题(深浅/配色/强调)与下载并发。</summary>
public sealed partial class SettingsPage : Page
{
    private static readonly string[] PaletteIds = ["salmon", "ink", "sand", "slate"];
    private static readonly string[] AccentIds = ["coral", "ocean", "forest", "violet", "amber"];

    private readonly AccountVM _account = new();
    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
        AccountRoot.DataContext = _account;
        LoadConfig();
    }

    private void LoadConfig()
    {
        _loading = true;
        var c = AppServices.Settings.Current;
        ThemeMode.SelectedIndex = c.Theme switch
        {
            Core.Settings.ThemePref.Light => 1,
            Core.Settings.ThemePref.Dark => 2,
            _ => 0,
        };
        var pi = Array.IndexOf(PaletteIds, c.Palette);
        PaletteList.SelectedIndex = pi < 0 ? 0 : pi;
        var ai = Array.IndexOf(AccentIds, c.Accent);
        AccentList.SelectedIndex = ai < 0 ? 0 : ai;
        ConcurrencyBox.Value = c.TransferConcurrency;
        _loading = false;
    }

    private void OnThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        AppServices.Settings.Update(c => c.Theme = ThemeMode.SelectedIndex switch
        {
            1 => Core.Settings.ThemePref.Light,
            2 => Core.Settings.ThemePref.Dark,
            _ => Core.Settings.ThemePref.FollowSystem,
        });
    }

    private void OnPaletteChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        var i = PaletteList.SelectedIndex;
        if (i < 0 || i >= PaletteIds.Length)
            return;
        AppServices.Settings.Update(c => c.Palette = PaletteIds[i]);
    }

    private void OnAccentChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading)
            return;
        var i = AccentList.SelectedIndex;
        if (i < 0 || i >= AccentIds.Length)
            return;
        AppServices.Settings.Update(c => c.Accent = AccentIds[i]);
    }

    private void OnConcurrencyChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue))
            return;
        AppServices.Settings.Update(c => c.TransferConcurrency = (int)Math.Clamp(args.NewValue, 2, 16));
    }
}
