using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace SL.App.Themes;

/// <summary>从一张壁纸里提取出的主题(配色 + 强调色 + 壁纸路径)。</summary>
public sealed record ImageThemeResult(string DominantHex, ThemePalette Palette, AccentChoice Accent);

/// <summary>从图片取色并生成主题:主色决定强调色,平均色决定底色倾向。</summary>
public static class ImageTheme
{
    /// <summary>读取图片像素(BGRA8)。</summary>
    public static async Task<(int Width, int Height, byte[] Pixels)?> ReadPixelsAsync(string path, int maxEdge = 320)
    {
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            using var ras = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(ras))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            ras.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(ras);
            var width = decoder.PixelWidth;
            var height = decoder.PixelHeight;
            var scale = Math.Min(1.0, (double)maxEdge / Math.Max(width, height));

            var transform = new BitmapTransform
            {
                ScaledWidth = Math.Max(1, (uint)(width * scale)),
                ScaledHeight = Math.Max(1, (uint)(height * scale)),
                InterpolationMode = BitmapInterpolationMode.Linear,
            };

            var data = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            return ((int)transform.ScaledWidth, (int)transform.ScaledHeight, data.DetachPixelData());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>平均色(用于判断整体明暗)。</summary>
    public static (byte R, byte G, byte B) Average(byte[] bgra)
    {
        long r = 0, g = 0, b = 0, n = 0;
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < 160)
                continue;
            b += bgra[i];
            g += bgra[i + 1];
            r += bgra[i + 2];
            n++;
        }
        if (n == 0)
            return (90, 100, 110);
        return ((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

    /// <summary>主色:按 16 级量化统计,选"出现多且足够鲜艳"的颜色。</summary>
    public static (byte R, byte G, byte B) Dominant(byte[] bgra)
    {
        var buckets = new Dictionary<int, (int Count, long R, long G, long B)>();
        for (var i = 0; i + 3 < bgra.Length; i += 4)
        {
            if (bgra[i + 3] < 200)
                continue;
            var b = bgra[i];
            var g = bgra[i + 1];
            var r = bgra[i + 2];

            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var lum = (max + min) / 2;
            if (lum < 24 || lum > 236)
                continue;

            var key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
            if (buckets.TryGetValue(key, out var entry))
                buckets[key] = (entry.Count + 1, entry.R + r, entry.G + g, entry.B + b);
            else
                buckets[key] = (1, r, g, b);
        }

        if (buckets.Count == 0)
            return (100, 116, 130);

        var bestScore = -1.0;
        (byte R, byte G, byte B) best = (100, 116, 130);
        foreach (var (_, entry) in buckets)
        {
            var r = (byte)(entry.R / entry.Count);
            var g = (byte)(entry.G / entry.Count);
            var b = (byte)(entry.B / entry.Count);

            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            var sat = max == 0 ? 0 : (max - min) / (double)max;
            var lum = (max + min) / 510.0;

            // 出现频率为主,适度偏好鲜艳、亮度居中的颜色
            var score = Math.Log(entry.Count + 1) * (0.35 + sat * 1.4) * (1 - Math.Abs(lum - 0.55) * 1.6);
            if (score > bestScore)
            {
                bestScore = score;
                best = (r, g, b);
            }
        }
        return best;
    }

    /// <summary>由图片生成主题(配色推导复用方块主题的 HSL 逻辑)。</summary>
    public static async Task<ImageThemeResult?> BuildAsync(string id, string name, string imagePath, string hint)
    {
        var pixels = await ReadPixelsAsync(imagePath);
        if (pixels is null)
            return null;

        var (r, g, b) = Dominant(pixels.Value.Pixels);
        var (ar, ag, ab) = Average(pixels.Value.Pixels);

        // 主色占 70%,平均色占 30%,避免极端鲜艳的画面把界面带偏
        var mr = (byte)((r * 0.7) + (ar * 0.3));
        var mg = (byte)((g * 0.7) + (ag * 0.3));
        var mb = (byte)((b * 0.7) + (ab * 0.3));

        var built = BlockTheme.Build(id, name, mr, mg, mb);
        var palette = built.Palette with { Hint = hint, Background = imagePath };
        var accent = built.Accent with { Name = name };
        return new ImageThemeResult($"#{r:X2}{g:X2}{b:X2}", palette, accent);
    }
}
