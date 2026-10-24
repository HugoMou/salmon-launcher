using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SL.App.Infrastructure;
using SL.App.Notify;
using SL.App.Themes;
using Windows.Storage;
using Windows.UI;

namespace SL.App.Pages;

/// <summary>一张方块卡(纹理、主色与状态可增量更新)。</summary>
public sealed class BlockCard : INotifyPropertyChanged
{
    private ImageSource? _thumb;
    private Brush _dot = new SolidColorBrush(Colors.Gray);
    private string _hex = "取色中…";
    private string _state = "正在获取纹理…";

    public required BlockSource Source { get; init; }

    public string Name => Source.Name;

    public string Hex
    {
        get => _hex;
        set => Set(ref _hex, value);
    }

    public string State
    {
        get => _state;
        set => Set(ref _state, value);
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

    public byte R { get; set; }
    public byte G { get; set; }
    public byte B { get; set; }
    public bool Ready { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>主题工坊:方块纹理取色 → 生成并应用主题。</summary>
public sealed partial class ThemeWorkshopPage : Page
{
    private readonly List<BlockCard> _cards = [];
    private bool _loaded;

    public ThemeWorkshopPage()
    {
        InitializeComponent();
        StatusText.Text = "准备中…";

        foreach (var block in BlockTheme.Blocks)
            _cards.Add(new BlockCard { Source = block });

        BlockGrid.ItemsSource = _cards;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (_loaded)
            return;
        _loaded = true;
        _ = LoadAllAsync();
    }

    private async Task LoadAllAsync()
    {
        RefreshButton.IsEnabled = false;
        StatusText.Text = "正在读取方块纹理(优先已安装客户端的原始贴图)…";

        await Task.WhenAll(_cards.Select(LoadCardAsync)).ConfigureAwait(true);

        var ready = _cards.Count(c => c.Ready);
        StatusText.Text = ready == 0
            ? "没有取到纹理:请检查网络,或先安装任意一个游戏版本后重试"
            : $"共 {_cards.Count} 个方块可用,已就绪 {ready} 个 · 点卡片即换肤";
        RefreshButton.IsEnabled = true;
    }

    private static async Task LoadCardAsync(BlockCard card)
    {
        try
        {
            var path = await BlockTheme.EnsureTextureAsync(card.Source).ConfigureAwait(true);
            if (path is null)
            {
                card.State = "无纹理";
                card.Hex = "—";
                return;
            }

            card.Thumb = await LoadBitmapAsync(path);

            var color = await BlockTheme.AverageColorAsync(path).ConfigureAwait(true);
            if (color is null)
            {
                card.State = "无法取色";
                card.Hex = "—";
                return;
            }

            card.R = color.Value.R;
            card.G = color.Value.G;
            card.B = color.Value.B;
            card.Ready = true;
            card.Hex = $"#{card.R:X2}{card.G:X2}{card.B:X2}";
            card.Dot = new SolidColorBrush(Color.FromArgb(255, card.R, card.G, card.B));
        }
        catch
        {
            card.State = "读取失败";
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

    // ---------------------------------------------------------- 交互

    private void OnBlockClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not BlockCard card)
            return;
        if (!card.Ready)
        {
            AppServices.Notify.Publish(NotifyKind.Warning, "这块纹理还没准备好,稍后再试");
            return;
        }

        var result = BlockTheme.Build(card.Source.Id, card.Source.Name, card.R, card.G, card.B);
        ThemeManager.RegisterPalette(result.Palette);
        ThemeManager.RegisterAccent(result.Accent);
        ThemeManager.SaveCustom(Core.Boot.Runtime.Root);

        AppServices.Settings.Update(c =>
        {
            c.Palette = result.Palette.Id;
            c.Accent = result.Accent.Id;
        });

        AppServices.Notify.Publish(NotifyKind.Success, $"已应用「{card.Name}」主题 · 主色 {result.DominantHex}");
    }

    private void OnResetTheme(object sender, RoutedEventArgs e)
    {
        AppServices.Settings.Update(c =>
        {
            c.Palette = "salmon";
            c.Accent = "coral";
        });
        AppServices.Notify.Publish(NotifyKind.Info, "已恢复默认配色");
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => _ = LoadAllAsync();
}
