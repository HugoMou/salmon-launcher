using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SL.App.Infrastructure;
using SL.App.Localization;
using SL.App.Notify;
using SL.App.Themes;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;

namespace SL.App.Pages;

/// <summary>一张壁纸场景卡。</summary>
public sealed class WallpaperCard : INotifyPropertyChanged
{
    private ImageSource? _thumb;
    private Brush _dot = new SolidColorBrush(Colors.Gray);
    private string _hex = "取色中…";
    private bool _ready;

    public required WallpaperScene Scene { get; init; }

    public string Name => Loc.T(Scene.Name);

    public string Hex
    {
        get => _hex;
        set => Set(ref _hex, value);
    }

    public ImageSource? Thumb
    {
        get => _thumb;
        set => Set(ref _thumb, value);
    }

    public Brush Dot
    {
        get => _dot;
        set => Set(ref _dot, value);
    }

    public bool Ready
    {
        get => _ready;
        set => Set(ref _ready, value);
    }

    public string? Path { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

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

    private void SyncCurrent()
    {
        var current = ThemeManager.Palettes.FirstOrDefault(p => p.Id == ThemeManager.PaletteId);
        var wallpaper = ThemeManager.BackgroundImage;
        CurrentText.Text = wallpaper is null
            ? Loc.T("当前壁纸") + ":—"
            : Loc.T("当前壁纸") + ":" + (current?.Name ?? System.IO.Path.GetFileName(wallpaper));
    }

    private async Task LoadAllAsync()
    {
        StatusText.Text = Loc.T("正在生成内置场景…");
        await Task.WhenAll(_cards.Select(LoadCardAsync)).ConfigureAwait(true);

        var ready = _cards.Count(c => c.Ready);
        StatusText.Text = ready == 0
            ? Loc.T("场景生成失败,请稍后重试")
            : string.Format(Loc.T("共 {0} 张场景,点击任意一张即可套用壁纸与配色"), ready);
    }

    private static async Task LoadCardAsync(WallpaperCard card)
    {
        try
        {
            var path = await WallpaperFactory.EnsureAsync(card.Scene);
            card.Path = path;
            card.Thumb = await LoadBitmapAsync(path);

            var pixels = await ImageTheme.ReadPixelsAsync(path);
            if (pixels is null)
                return;

            var (r, g, b) = ImageTheme.Dominant(pixels.Value.Pixels);
            card.Hex = $"#{r:X2}{g:X2}{b:X2}";
            card.Dot = new SolidColorBrush(Color.FromArgb(255, r, g, b));
            card.Ready = true;
        }
        catch
        {
            card.Hex = "—";
        }
    }

    private static async Task<ImageSource?> LoadBitmapAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenAsync(FileAccessMode.Read);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            return image;
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------- 应用主题

    private async void OnSceneClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not WallpaperCard card || !card.Ready || card.Path is null)
        {
            AppServices.Notify.Publish(NotifyKind.Warning, Loc.T("这张场景还没准备好,稍后再试"));
            return;
        }

        await ApplyAsync($"wp-{card.Scene.Id}", Loc.T(card.Scene.Name), card.Path, Loc.T("内置场景")).ConfigureAwait(true);
    }

    private async void OnPickImage(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            foreach (var ext in new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" })
                picker.FileTypeFilter.Add(ext);

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            var local = await WallpaperFactory.ImportAsync(file.Path);
            await ApplyAsync($"wp-{System.IO.Path.GetFileNameWithoutExtension(local)}",
                System.IO.Path.GetFileNameWithoutExtension(file.Name), local, Loc.T("从图片创建"))
                .ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppServices.Notify.Publish(NotifyKind.Error, ex.Message);
        }
    }

    private async Task ApplyAsync(string id, string name, string imagePath, string hint)
    {
        var result = await ImageTheme.BuildAsync(id, name, imagePath, hint).ConfigureAwait(true);
        if (result is null)
        {
            AppServices.Notify.Publish(NotifyKind.Error, Loc.T("这张图片无法读取,换一张试试"));
            return;
        }

        ThemeManager.RegisterPalette(result.Palette);
        ThemeManager.RegisterAccent(result.Accent);
        ThemeManager.SaveCustom(Core.Boot.Runtime.Root);

        AppServices.Settings.Update(c =>
        {
            c.Palette = result.Palette.Id;
            c.Accent = result.Accent.Id;
        });

        SyncCurrent();
        AppServices.Notify.Publish(NotifyKind.Success,
            string.Format(Loc.T("已应用「{0}」主题 · 主色 {1}"), name, result.DominantHex));
    }

    private void OnClearWallpaper(object sender, RoutedEventArgs e)
    {
        AppServices.Settings.Update(c => c.Palette = "salmon");
        SyncCurrent();
        AppServices.Notify.Publish(NotifyKind.Info, Loc.T("已清除壁纸,恢复默认配色"));
    }

    private void OnResetTheme(object sender, RoutedEventArgs e)
    {
        AppServices.Settings.Update(c =>
        {
            c.Palette = "salmon";
            c.Accent = "coral";
        });
        SyncCurrent();
        AppServices.Notify.Publish(NotifyKind.Info, Loc.T("已恢复默认配色"));
    }
}
