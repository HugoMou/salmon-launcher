// Salmon Launcher (SL) — a Minecraft Java edition launcher
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
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Windowing;
using SL.App.Dialogs;
using SL.App.Infrastructure;
using SL.App.Localization;
using SL.App.Navigation;
using SL.App.Notify;
using SL.App.Themes;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SL.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        Title = "Salmon Launcher";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        SystemBackdrop = new MicaBackdrop();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 980;
            presenter.PreferredMinimumHeight = 640;
        }
        AppWindow.Resize(new SizeInt32(1320, 860));
        SetWindowIcon();

        BuildMenu();
        Loc.ApplyTo(RootGrid);
        NotifyHost.ItemsSource = AppServices.Notify.Items;

        RootGrid.Loaded += async (_, _) =>
        {
            AppServices.Dialogs.Attach(RootGrid.XamlRoot);
            Loc.ApplyTo(RootGrid);
            if (AppServices.Settings.Current.OnboardingCompleted)
                await EnsureTermsAsync();
            else
                Onboarding.Visibility = Visibility.Visible;
        };
        Onboarding.Completed += async () => await EnsureTermsAsync();
        AppServices.Nav.CurrentChanged += () => DispatcherQueue.TryEnqueue(SyncShell);
        ThemeManager.ThemeChanged += () => DispatcherQueue.TryEnqueue(ApplyTheme);
        Loc.Changed += () => DispatcherQueue.TryEnqueue(OnLanguageChanged);

        AppServices.Nav.Attach(NavFrame);
        SyncShell();
        ApplyTheme();
    }

    // ---------------------------------------------------------- 窗口图标

    /// <summary>任务栏 / Alt+Tab 图标:项目内置的 Assets\Logo.ico(含 16–256 多尺寸)。</summary>
    private void SetWindowIcon()
    {
        try
        {
            var icon = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Logo.ico");
            if (System.IO.File.Exists(icon))
                AppWindow.SetIcon(icon);
        }
        catch (Exception ex)
        {
            Core.Logging.AppLog.Warn("Shell", "设置窗口图标失败:" + ex.Message);
        }
    }

    // ---------------------------------------------------------- 首次启动协议

    private async Task EnsureTermsAsync()
    {
        if (AppServices.Settings.Current.TermsAccepted)
            return;

        var agreed = await AppServices.Dialogs.ConfirmAsync(
            Loc.T("用户协议与免责声明"),
            Loc.T("Salmon Launcher 为第三方免费工具,与 Mojang Studios、Microsoft 无隶属或授权关系;Minecraft 是 Mojang Studios 的商标。")
            + "\n\n"
            + Loc.T("本软件不含任何统计、埋点或广告组件,不会收集你的设备信息、游戏行为或个人信息。仅在排查问题时,你主动分享的日志文件会被读取。")
            + "\n\n"
            + $"{Loc.T("版本")} {Core.Boot.Runtime.AppVersion} · {Loc.T("使用本软件即表示你已阅读并接受以下全部条款。")}",
            MsgMark.Info,
            Loc.T("同意并继续"),
            Loc.T("关闭")).ConfigureAwait(true);

        if (!agreed)
        {
            Application.Current.Exit();
            return;
        }

        AppServices.Settings.Update(c => c.TermsAccepted = true);
        AppServices.Notify.Publish(NotifyKind.Success, Loc.T("同意并继续"));
    }

    // ---------------------------------------------------------- 导航菜单

    private void BuildMenu()
    {
        NavView.MenuItems.Clear();
        foreach (var group in NavGroups.All)
        {
            NavView.MenuItems.Add(new NavigationViewItemHeader { Content = Loc.T(group.Title) });
            foreach (var item in group.Items)
            {
                NavView.MenuItems.Add(new NavigationViewItem
                {
                    Content = Loc.T(item.Label),
                    Tag = item.Key,
                    Icon = new FontIcon { Glyph = item.Glyph },
                });
            }
        }
    }

    private void OnNavItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string key)
            AppServices.Nav.Navigate(key);
    }

    /// <summary>导航变化 → 同步面包屑与 NavigationView 选中态。</summary>
    private void SyncShell()
    {
        var key = AppServices.Nav.CurrentKey;
        var spec = PageCatalog.Get(key);

        PageTitleText.Text = $"· {Loc.T(spec.Title)}";

        var item = FindItem(NavView.MenuItems, key);
        if (item is not null && !ReferenceEquals(NavView.SelectedItem, item))
            NavView.SelectedItem = item;
    }

    private static NavigationViewItem? FindItem(IList<object> items, string tag)
    {
        foreach (var obj in items)
        {
            if (obj is not NavigationViewItem nvi)
                continue;
            if (Equals(nvi.Tag, tag))
                return nvi;
            if (nvi.MenuItems.Count > 0 && FindItem(nvi.MenuItems, tag) is { } inner)
                return inner;
        }
        return null;
    }

    // ---------------------------------------------------------- 语言

    private void OnLanguageChanged()
    {
        BuildMenu();
        SyncShell();
        Loc.ApplyTo(RootGrid);
        AppServices.Nav.ReloadCurrent();
    }

    // ---------------------------------------------------------- 主题与壁纸

    private void ApplyTheme()
    {
        RootGrid.RequestedTheme = ThemeManager.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        WallpaperScrim.Background = ThemedBrush("Brush.WallpaperScrim");
        ApplyWallpaper();
    }

    private async void ApplyWallpaper()
    {
        var path = ThemeManager.BackgroundImage;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            WallpaperLayer.Visibility = Visibility.Collapsed;
            WallpaperBrush.ImageSource = null;
            SetContentBackground(ThemedBrush("Brush.PageBg"));
            return;
        }

        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(stream);

            WallpaperBrush.ImageSource = bitmap;
            WallpaperLayer.Visibility = Visibility.Visible;
            SetContentBackground(ThemedBrush("Brush.PageBgTrans"));
        }
        catch
        {
            WallpaperLayer.Visibility = Visibility.Collapsed;
            SetContentBackground(ThemedBrush("Brush.PageBg"));
        }
    }

    /// <summary>顶部提示区与页面区共用一层底色,避免壁纸在标题下露出色带。</summary>
    private void SetContentBackground(Brush? brush)
    {
        ContentRoot.Background = brush;
        PageHost.Background = brush;
    }

    /// <summary>从当前主题字典取画刷。</summary>
    private static Brush? ThemedBrush(string key)
    {
        var resources = Application.Current.Resources;
        var dictKey = ThemeManager.IsDark ? "Default" : "Light";
        if (resources.ThemeDictionaries.TryGetValue(dictKey, out var dict)
            && dict is ResourceDictionary rd
            && rd.TryGetValue(key, out var value)
            && value is Brush brush)
            return brush;
        return null;
    }
}
