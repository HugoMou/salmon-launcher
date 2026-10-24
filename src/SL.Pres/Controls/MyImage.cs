using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SL.Pres.Infrastructure;

namespace SL.Pres.Controls;

/// <summary>
/// 在标准 Image 基础上增加:
/// - MySource:网络 URL 或本地路径的异步加载(网络走 Core 图片缓存服务);
/// - PlaceholderSource / FallbackSource:加载占位与失败回退;
/// - WebP 等扩展解码通过 MyBitmap.DecodeAny / DecodeHook 支持。
/// 控件不承载圆角等视觉变化,由外层控件负责。
/// </summary>
public class MyImage : Image
{
    private int _generation;
    private string? _loadedSource;

    public static readonly DependencyProperty MySourceProperty = DependencyProperty.Register(
        nameof(MySource), typeof(string), typeof(MyImage),
        new PropertyMetadata(string.Empty, OnMySourceChanged));

    public string MySource
    {
        get => (string)GetValue(MySourceProperty);
        set => SetValue(MySourceProperty, value);
    }

    public static readonly DependencyProperty PlaceholderSourceProperty = DependencyProperty.Register(
        nameof(PlaceholderSource), typeof(ImageSource), typeof(MyImage), new PropertyMetadata(null));

    public ImageSource? PlaceholderSource
    {
        get => (ImageSource?)GetValue(PlaceholderSourceProperty);
        set => SetValue(PlaceholderSourceProperty, value);
    }

    public static readonly DependencyProperty FallbackSourceProperty = DependencyProperty.Register(
        nameof(FallbackSource), typeof(ImageSource), typeof(MyImage), new PropertyMetadata(null));

    public ImageSource? FallbackSource
    {
        get => (ImageSource?)GetValue(FallbackSourceProperty);
        set => SetValue(FallbackSourceProperty, value);
    }

    public MyImage()
    {
        Loaded += (_, _) => BeginLoad();
        Unloaded += (_, _) => _generation++;
    }

    private static void OnMySourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((MyImage)d).BeginLoad();

    private void BeginLoad()
    {
        var source = MySource;
        var generation = ++_generation;

        if (string.IsNullOrWhiteSpace(source))
        {
            Source = PlaceholderSource;
            _loadedSource = "";
            return;
        }

        Source = PlaceholderSource ?? Source;

        // 同一来源且已成功加载过:跳过重复解析
        if (source == _loadedSource && Source != null && !Equals(Source, PlaceholderSource))
            return;

        _ = LoadAsync(source, generation);
    }

    private async Task LoadAsync(string source, int generation)
    {
        try
        {
            BitmapSource? bmp;
            if (source.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                var bytes = await AppServices.Images.GetOrFetchAsync(source).ConfigureAwait(false);
                bmp = await Task.Run(() => CoreImaging(bytes)).ConfigureAwait(false);
            }
            else
            {
                bmp = await Task.Run(() => MyBitmap.LoadLocal(source)).ConfigureAwait(false);
                if (bmp is null)
                    throw new InvalidOperationException("本地文件不存在或无法解析");
            }

            if (generation != _generation)
                return;

            await Dispatcher.BeginInvoke(() =>
            {
                if (generation != _generation)
                    return;
                _loadedSource = source;
                Source = bmp;
            });
        }
        catch (Exception ex)
        {
            Core.Logging.AppLog.Warn("MyImage", $"{source} 加载失败:{ex.Message}");
            await Dispatcher.BeginInvoke(() =>
            {
                if (generation != _generation)
                    return;
                Source = FallbackSource ?? PlaceholderSource;
                _loadedSource = source;
            });
        }
    }

    /// <summary>解码在后台线程执行(Freeze 后可跨线程)</summary>
    private static BitmapSource? CoreImaging(byte[] bytes) => MyBitmap.DecodeAny(bytes);
}
