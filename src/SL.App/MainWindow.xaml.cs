using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using SL.App.Dialogs;
using SL.App.Infrastructure;
using SL.App.Navigation;
using SL.App.Notify;
using SL.App.Themes;
using Windows.Graphics;

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
        AppWindow.Resize(new SizeInt32(1240, 800));

        BuildMenu();
        NotifyHost.ItemsSource = AppServices.Notify.Items;

        RootGrid.Loaded += async (_, _) =>
        {
            AppServices.Dialogs.Attach(RootGrid.XamlRoot);
            await EnsureTermsAsync();
        };
        AppServices.Nav.CurrentChanged += () => DispatcherQueue.TryEnqueue(SyncShell);
        ThemeManager.ThemeChanged += () => DispatcherQueue.TryEnqueue(ApplyTheme);

        AppServices.Nav.Attach(NavFrame);
        SyncShell();
    }

    // ---------------------------------------------------------- 首次启动协议

    private async Task EnsureTermsAsync()
    {
        if (AppServices.Settings.Current.TermsAccepted)
            return;

        var agreed = await AppServices.Dialogs.ConfirmAsync(
            "用户协议与免责声明",
            "Salmon Launcher 是第三方免费工具,与 Mojang Studios、Microsoft 无隶属关系。\n\n"
            + "启动器只从官方源或其镜像下载游戏文件,账户令牌仅保存在本机,不含任何统计与广告组件。\n\n"
            + "继续使用即表示你已阅读并同意《用户协议与免责声明》,可在「设置 → 用户协议」中随时查看。",
            MsgMark.Info,
            "同意并继续",
            "退出").ConfigureAwait(true);

        if (!agreed)
        {
            Application.Current.Exit();
            return;
        }

        AppServices.Settings.Update(c => c.TermsAccepted = true);
        AppServices.Notify.Publish(NotifyKind.Success, "已同意用户协议,开始使用吧");
    }

    // ---------------------------------------------------------- 导航菜单

    private void BuildMenu()
    {
        foreach (var group in NavGroups.All)
        {
            NavView.MenuItems.Add(new NavigationViewItemHeader { Content = group.Title });
            foreach (var item in group.Items)
            {
                NavView.MenuItems.Add(new NavigationViewItem
                {
                    Content = item.Label,
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

    /// <summary>导航变化 → 同步标题、面包屑与 NavigationView 选中态。</summary>
    private void SyncShell()
    {
        var key = AppServices.Nav.CurrentKey;
        var spec = PageCatalog.Get(key);

        HeaderText.Text = spec.Title;
        PageTitleText.Text = $"· {spec.Title}";

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

    // ---------------------------------------------------------- 主题

    private void ApplyTheme()
        => RootGrid.RequestedTheme = ThemeManager.IsDark ? ElementTheme.Dark : ElementTheme.Light;
}
