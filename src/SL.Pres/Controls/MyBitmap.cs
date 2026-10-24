using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SL.Pres.Controls;

/// <summary>
/// 系统自带 codecs 以外的位图工具层:格式解码、缩放/旋转/翻转/裁剪与内存位图缓存。
/// WebP 等扩展格式通过 DecodeHook 插入(阶段五接入皮肤纹理时联动)。
/// </summary>
public static class MyBitmap
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, BitmapImage> Cache = [];
    private static readonly List<string> CacheOrder = [];
    private const int CacheCapacity = 96;

    /// <summary>外部解码器挂接点:返回 null 表示不支持,回落到系统编解码器。</summary>
    public static Func<byte[], BitmapSource?>? DecodeHook { get; set; }

    /// <summary>从字节内存解码(可安全在后台线程执行,结果 Freezed)。</summary>
    public static BitmapImage Decode(byte[] data)
    {
        var bmp = new BitmapImage();
        using var stream = new MemoryStream(data);
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = stream;
        bmp.EndInit();
        bmp.Freeze(); // 跨线程安全
        return bmp;
    }

    /// <summary>从字节解码,优先走解码钩子(例如 WebP 扩展)。</summary>
    public static BitmapSource? DecodeAny(byte[] data)
    {
        if (data.Length >= 12)
        {
            var magic = System.Text.Encoding.ASCII.GetString(data, 0, 4);
            if (magic == "RIFF" && System.Text.Encoding.ASCII.GetString(data, 8, 4) == "WEBP")
                return DecodeHook?.Invoke(data);
        }
        return Decode(data);
    }

    /// <summary>本地文件解码 + 内存缓存。</summary>
    public static BitmapImage? LoadLocal(string path)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(path, out var hit))
                return hit;
        }

        if (!File.Exists(path))
            return null;

        BitmapImage bmp;
        try
        {
            bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
        }
        catch
        {
            return null;
        }

        lock (Gate)
        {
            Cache[path] = bmp;
            CacheOrder.Add(path);
            if (CacheOrder.Count > CacheCapacity)
            {
                var oldest = CacheOrder[0];
                CacheOrder.RemoveAt(0);
                Cache.Remove(oldest);
            }
        }
        return bmp;
    }

    public static BitmapSource Rotate(BitmapSource source, double degrees)
    {
        var rotated = new TransformedBitmap(source, new RotateTransform(degrees));
        rotated.Freeze();
        return rotated;
    }

    public static BitmapSource Flip(BitmapSource source, bool flipX, bool flipY)
    {
        if (!flipX && !flipY)
            return source;
        var flipped = new TransformedBitmap(source, new ScaleTransform(flipX ? -1 : 1, flipY ? -1 : 1));
        flipped.Freeze();
        return flipped;
    }

    public static BitmapSource Crop(BitmapSource source, int x, int y, int width, int height)
    {
        var cropped = new CroppedBitmap(source, new Int32Rect(x, y, width, height));
        cropped.Freeze();
        return cropped;
    }

    public static byte[] ToPngBytes(BitmapSource source)
    {
        using var ms = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static BitmapSource Resize(BitmapSource source, int pixelWidth, int pixelHeight)
    {
        var scaled = new TransformedBitmap(source, new ScaleTransform(
            pixelWidth / (double)source.PixelWidth, pixelHeight / (double)source.PixelHeight));
        scaled.Freeze();
        return scaled;
    }
}
