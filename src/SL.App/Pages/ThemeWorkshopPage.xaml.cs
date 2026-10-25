using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SL.App.Localization;
using SL.App.Themes;

namespace SL.App.Pages;

/// <summary>主题工坊:游戏画面壁纸 + 自动取色,一起做成整套主题。</summary>
public sealed partial class ThemeWorkshopPage : Page
{
    private readonly List<WallpaperCard> _cards = [];
    private bool _loaded;

    public ThemeWorkshopPage()
    {
        InitializeComponent();
        StatusText.Text = Loc.T("正在准备壁纸…");

        foreach (var scene in WallpaperFactory.Scenes)
            _cards.Add(new WallpaperCard { Scene = scene });

        SceneGrid.ItemsSource = _cards;
        SyncCurrent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SyncCurrent();
        if (_loaded)
            return;
        _loaded = true;
        _ = LoadAllAsync();
    }

    private void SyncCurrent() => CurrentText.Text = ThemeApplier.CurrentSummary();

    private async Task LoadAllAsync()
    {
        StatusText.Text = Loc.T("正在生成内置场景…");
        await Task.WhenAll(_cards.Select(ThemeApplier.PrepareAsync)).ConfigureAwait(true);

        var ready = _cards.Count(c => c.Ready);
        StatusText.Text = ready == 0
            ? Loc.T("场景生成失败,请稍后重试")
            : string.Format(Loc.T("共 {0} 张场景,点击任意一张即可套用壁纸与配色"), ready);
    }

    private async void OnSceneClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not WallpaperCard card)
            return;
        if (await ThemeApplier.ApplySceneAsync(card).ConfigureAwait(true))
            SyncCurrent();
    }

    private async void OnPickImage(object sender, RoutedEventArgs e)
    {
        if (await ThemeApplier.PickAndApplyAsync().ConfigureAwait(true))
            SyncCurrent();
    }

    private void OnClearWallpaper(object sender, RoutedEventArgs e)
    {
        ThemeApplier.ClearWallpaper();
        SyncCurrent();
    }

    private void OnResetTheme(object sender, RoutedEventArgs e)
    {
        ThemeApplier.ResetPalette();
        SyncCurrent();
    }
}