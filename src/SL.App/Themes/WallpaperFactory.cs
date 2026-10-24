using Windows.Graphics.Imaging;
using Windows.Storage;

namespace SL.App.Themes;

/// <summary>一个内置壁纸场景。</summary>
public sealed record WallpaperScene(string Id, string Name, string FileName);

/// <summary>
/// 内置 Minecraft 风格壁纸:用像素画方式绘制天空、云、地形与植被,
/// 首次使用时生成 PNG 缓存,再交给 ImageTheme 取色成主题。
/// </summary>
public static class WallpaperFactory
{
    public const int Width = 640;
    public const int Height = 360;

    public static IReadOnlyList<WallpaperScene> Scenes { get; } =
    [
        new("plains", "草原清晨", "plains-dawn.png"),
        new("desert", "沙漠正午", "desert-noon.png"),
        new("snow", "雪原黄昏", "snow-dusk.png"),
        new("cherry", "樱花山谷", "cherry-valley.png"),
        new("nether", "下界熔岩", "nether-lava.png"),
        new("end", "末地星空", "end-sky.png"),
        new("ocean", "海面日落", "ocean-sunset.png"),
        new("mushroom", "蘑菇夜林", "mushroom-night.png"),
        new("village", "村庄麦田", "village-field.png"),
        new("cave", "萤石矿洞", "cave-glow.png"),
    ];

    public static string Dir
    {
        get
        {
            var dir = Path.Combine(Core.Boot.Runtime.Root, "themes", "wallpapers");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>确保壁纸已生成,返回本地路径。</summary>
    public static async Task<string> EnsureAsync(WallpaperScene scene)
    {
        var path = Path.Combine(Dir, scene.FileName);
        if (File.Exists(path) && new FileInfo(path).Length > 2048)
            return path;

        var canvas = new Canvas(Width, Height);
        switch (scene.Id)
        {
            case "desert": Desert(canvas); break;
            case "snow": Snow(canvas); break;
            case "cherry": Cherry(canvas); break;
            case "nether": Nether(canvas); break;
            case "end": End(canvas); break;
            case "ocean": Ocean(canvas); break;
            case "mushroom": Mushroom(canvas); break;
            case "village": Village(canvas); break;
            case "cave": Cave(canvas); break;
            default: Plains(canvas); break;
        }

        await WritePngAsync(path, canvas.Pixels, Width, Height);
        return path;
    }

    /// <summary>把用户选的图片复制进壁纸库,返回副本路径。</summary>
    public static async Task<string> ImportAsync(string sourcePath)
    {
        var name = $"user-{DateTime.Now:yyyyMMdd-HHmmss}-{Path.GetFileNameWithoutExtension(sourcePath)}";
        var dest = Path.Combine(Dir, Sanitize(name) + Path.GetExtension(sourcePath).ToLowerInvariant());
        await using var input = File.OpenRead(sourcePath);
        await using var output = File.Create(dest);
        await input.CopyToAsync(output);
        return dest;
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars);
    }

    private static async Task WritePngAsync(string path, byte[] bgra, int width, int height)
    {
        if (!File.Exists(path))
            await File.WriteAllBytesAsync(path, []);

        var file = await StorageFile.GetFileFromPathAsync(path);
        using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
        stream.Size = 0;

        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            (uint)width, (uint)height, 96, 96, bgra);
        await encoder.FlushAsync();
    }

    // ============================================================ 像素画布

    private sealed class Canvas
    {
        private readonly Random _rng;

        public Canvas(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new byte[width * height * 4];
            _rng = new Random(20260101);
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Pixels { get; }

        public void Set(int x, int y, byte r, byte g, byte b)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height)
                return;
            var i = (y * Width + x) * 4;
            Pixels[i] = b;
            Pixels[i + 1] = g;
            Pixels[i + 2] = r;
            Pixels[i + 3] = 255;
        }

        public void Blend(int x, int y, byte r, byte g, byte b, double alpha)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height || alpha <= 0)
                return;
            var i = (y * Width + x) * 4;
            var a = Math.Clamp(alpha, 0, 1);
            Pixels[i] = (byte)(Pixels[i] * (1 - a) + b * a);
            Pixels[i + 1] = (byte)(Pixels[i + 1] * (1 - a) + g * a);
            Pixels[i + 2] = (byte)(Pixels[i + 2] * (1 - a) + r * a);
            Pixels[i + 3] = 255;
        }

        public void Rect(int x, int y, int w, int h, (byte R, byte G, byte B) color)
        {
            for (var dy = 0; dy < h; dy++)
                for (var dx = 0; dx < w; dx++)
                    Set(x + dx, y + dy, color.R, color.G, color.B);
        }

        public void RectAlpha(int x, int y, int w, int h, (byte R, byte G, byte B) color, double alpha)
        {
            for (var dy = 0; dy < h; dy++)
                for (var dx = 0; dx < w; dx++)
                    Blend(x + dx, y + dy, color.R, color.G, color.B, alpha);
        }

        /// <summary>竖直渐变天空。</summary>
        public void Sky((byte R, byte G, byte B) top, (byte R, byte G, byte B) bottom)
        {
            for (var y = 0; y < Height; y++)
            {
                var t = y / (double)(Height - 1);
                var r = (byte)(top.R + (bottom.R - top.R) * t);
                var g = (byte)(top.G + (bottom.G - top.G) * t);
                var b = (byte)(top.B + (bottom.B - top.B) * t);
                for (var x = 0; x < Width; x++)
                    Set(x, y, r, g, b);
            }
        }

        /// <summary>像素圆(太阳/月亮)。</summary>
        public void Disc(int cx, int cy, int radius, (byte R, byte G, byte B) color, double alpha = 1)
        {
            for (var y = -radius; y <= radius; y++)
                for (var x = -radius; x <= radius; x++)
                {
                    if (x * x + y * y > radius * radius)
                        continue;
                    Blend(cx + x, cy + y, color.R, color.G, color.B, alpha);
                }
        }

        /// <summary>云朵:若干横向方块叠出 Minecraft 的方云。</summary>
        public void Cloud(int x, int y, int scale, double alpha)
        {
            var blocks = new (int X, int Y, int W, int H)[]
            {
                (0, 0, 5, 1), (1, -1, 3, 1), (4, 1, 3, 1), (-1, 1, 2, 1),
            };
            foreach (var (bx, by, bw, bh) in blocks)
                RectAlpha(x + bx * scale, y + by * scale, bw * scale, bh * scale, (255, 255, 255), alpha);
        }

        /// <summary>远景山:梯形/三角形堆叠。</summary>
        public void Hill(int cx, int baseY, int halfWidth, int peak, (byte R, byte G, byte B) color, double alpha = 1)
        {
            for (var y = 0; y < peak; y++)
            {
                var t = y / (double)peak;
                var w = (int)(halfWidth * (1 - t));
                for (var x = cx - w; x <= cx + w; x++)
                    Blend(x, baseY - y, color.R, color.G, color.B, alpha);
            }
        }

        /// <summary>方块噪点地面(草地/沙地/雪地)。</summary>
        public void Ground(int top, (byte R, byte G, byte B) baseColor, (byte R, byte G, byte B) altColor, int block = 8)
        {
            for (var y = top; y < Height; y += block)
                for (var x = 0; x < Width; x += block)
                {
                    var pick = _rng.NextDouble();
                    var color = pick < 0.45 ? altColor : baseColor;
                    var shade = 0.92 + _rng.NextDouble() * 0.16;
                    var r = (byte)Math.Clamp(color.R * shade, 0, 255);
                    var g = (byte)Math.Clamp(color.G * shade, 0, 255);
                    var b = (byte)Math.Clamp(color.B * shade, 0, 255);
                    Rect(x, y, block, Math.Min(block, Height - y), (r, g, b));
                }
        }

        /// <summary>Minecraft 风格的树:一根树干 + 方块状树冠。</summary>
        public void Tree(int x, int groundY, int scale, (byte R, byte G, byte B) leaf, (byte R, byte G, byte B) leafDark,
            (byte R, byte G, byte B) trunk)
        {
            var trunkW = Math.Max(3, scale);
            var trunkH = 5 * scale;
            Rect(x - trunkW / 2, groundY - trunkH, trunkW, trunkH, trunk);

            var crownW = 7 * scale;
            var crownH = 5 * scale;
            var crownY = groundY - trunkH - crownH + scale;
            RectAlpha(x - crownW / 2, crownY, crownW, crownH, leaf, 1);
            RectAlpha(x - crownW / 2 + scale, crownY - scale, crownW - scale * 2, scale, leaf, 1);
            RectAlpha(x - crownW / 2 + scale, crownY + crownH - scale, crownW - scale * 2, scale / 2 + 1, leafDark, 0.85);
        }

        public void Stars(int count, double alpha)
        {
            for (var i = 0; i < count; i++)
            {
                var x = _rng.Next(0, Width);
                var y = _rng.Next(0, Height / 2);
                var size = _rng.NextDouble() < 0.85 ? 1 : 2;
                RectAlpha(x, y, size, size, (255, 255, 255), alpha * (0.4 + _rng.NextDouble() * 0.6));
            }
        }
    }

    // ============================================================ 场景

    private static void Plains(Canvas c)
    {
        c.Sky((0x6F, 0xA8, 0xDE), (0xCF, 0xE6, 0xF5));
        c.Disc(520, 72, 26, (0xFF, 0xF4, 0xC8), 0.95);
        c.Cloud(70, 52, 7, 0.75);
        c.Cloud(300, 34, 9, 0.6);
        c.Cloud(470, 96, 6, 0.55);
        c.Hill(160, 214, 160, 62, (0x7C, 0x9E, 0x74), 0.9);
        c.Hill(470, 214, 200, 44, (0x6C, 0x8F, 0x68), 0.95);
        c.Ground(212, (0x5E, 0x9C, 0x3C), (0x76, 0xB5, 0x4A));
        c.Tree(120, 300, 6, (0x3E, 0x7A, 0x2E), (0x2F, 0x5F, 0x24), (0x6B, 0x4B, 0x2A));
        c.Tree(520, 330, 8, (0x44, 0x82, 0x32), (0x33, 0x63, 0x27), (0x5E, 0x42, 0x26));
    }

    private static void Desert(Canvas c)
    {
        c.Sky((0x5F, 0xAF, 0xE6), (0xE3, 0xEF, 0xF6));
        c.Disc(300, 60, 30, (0xFF, 0xFA, 0xD8), 1);
        c.Ground(196, (0xE2, 0xC9, 0x7C), (0xD2, 0xB4, 0x63));
        c.Hill(120, 208, 150, 30, (0xD8, 0xBE, 0x72), 1);
        c.Hill(520, 208, 170, 22, (0xCD, 0xB2, 0x66), 1);
        c.Rect(430, 236, 12, 60, (0x4E, 0x7A, 0x3A));
        c.Rect(414, 250, 12, 10, (0x55, 0x84, 0x40));
        c.Rect(446, 268, 12, 10, (0x55, 0x84, 0x40));
        c.Rect(196, 300, 10, 42, (0x4E, 0x7A, 0x3A));
    }

    private static void Snow(Canvas c)
    {
        c.Sky((0x3E, 0x54, 0x8C), (0xF0, 0x9F, 0x64));
        c.Disc(470, 150, 22, (0xFF, 0xD9, 0x9B), 0.9);
        c.Cloud(90, 46, 8, 0.5);
        c.Hill(180, 220, 190, 70, (0x9A, 0xB3, 0xCB), 0.9);
        c.Hill(470, 220, 200, 52, (0x86, 0xA2, 0xBE), 0.9);
        c.Ground(218, (0xF2, 0xF8, 0xFD), (0xDD, 0xEA, 0xF5));
        c.Tree(140, 292, 6, (0x2E, 0x51, 0x44), (0x24, 0x40, 0x36), (0x4A, 0x38, 0x2A));
        c.Tree(500, 320, 7, (0x2E, 0x51, 0x44), (0x24, 0x40, 0x36), (0x4A, 0x38, 0x2A));
        for (var i = 0; i < 220; i++)
        {
            var x = 20 + (i * 37) % 600;
            var y = 200 + (i * 53) % 150;
            c.Rect(x, y, 2, 2, (255, 255, 255));
        }
    }

    private static void Cherry(Canvas c)
    {
        c.Sky((0xF6, 0xC6, 0xD8), (0xBE, 0xE2, 0xF7));
        c.Cloud(120, 40, 7, 0.7);
        c.Cloud(420, 70, 6, 0.6);
        c.Hill(300, 210, 260, 40, (0xB7, 0xD2, 0xA8), 0.85);
        c.Ground(206, (0x6E, 0xA8, 0x50), (0x83, 0xBC, 0x62));
        c.Tree(110, 300, 7, (0xF2, 0xB4, 0xCE), (0xE1, 0x93, 0xB4), (0x6E, 0x4A, 0x33));
        c.Tree(520, 322, 8, (0xF6, 0xC1, 0xD8), (0xE6, 0x9C, 0xBC), (0x6E, 0x4A, 0x33));
        for (var i = 0; i < 90; i++)
        {
            var x = (i * 61) % 640;
            var y = (i * 29) % 340;
            c.Blend(x, y, 0xF7, 0xC9, 0xDC, 0.75);
        }
    }

    private static void Nether(Canvas c)
    {
        c.Sky((0x3A, 0x0E, 0x0E), (0x7A, 0x1B, 0x12));
        c.Hill(120, 240, 200, 90, (0x4A, 0x1C, 0x18), 1);
        c.Hill(520, 240, 220, 110, (0x40, 0x17, 0x14), 1);
        c.Ground(238, (0x5A, 0x2A, 0x24), (0x6A, 0x33, 0x2A));
        c.Rect(0, 316, 640, 44, (0xE8, 0x58, 0x1A));
        c.Rect(0, 330, 640, 30, (0xFF, 0x8A, 0x1F));
        c.Rect(0, 344, 640, 16, (0xFF, 0xC2, 0x4B));
        for (var i = 0; i < 60; i++)
        {
            var x = (i * 47) % 640;
            var y = 300 + (i * 13) % 40;
            c.Blend(x, y, 0xFF, 0xE6, 0x8A, 0.8);
        }
    }

    private static void End(Canvas c)
    {
        c.Sky((0x08, 0x08, 0x10), (0x1C, 0x1B, 0x2C));
        c.Stars(300, 0.9);
        c.Disc(120, 60, 20, (0xE8, 0xE6, 0xC8), 0.95);
        c.Hill(320, 230, 300, 60, (0x2A, 0x28, 0x36), 1);
        c.Ground(228, (0xD8, 0xD4, 0x9C), (0xC7, 0xC2, 0x88));
        c.Rect(250, 150, 12, 80, (0xC9, 0xB6, 0xE0));
        c.Rect(380, 176, 10, 54, (0xBE, 0xA8, 0xD8));
        c.Rect(250, 150, 12, 8, (0xE6, 0xD8, 0xF4));
        c.Rect(380, 176, 10, 6, (0xE6, 0xD8, 0xF4));
    }

    private static void Ocean(Canvas c)
    {
        c.Sky((0x5C, 0x3B, 0x6E), (0xF4, 0x9B, 0x53));
        c.Disc(320, 176, 34, (0xFF, 0xD3, 0x8A), 0.95);
        c.Cloud(100, 60, 8, 0.45);
        c.Cloud(430, 44, 7, 0.4);
        c.Hill(140, 208, 160, 26, (0x6E, 0x4B, 0x7C), 0.9);
        for (var i = 0; i < 34; i++)
        {
            var y = 206 + i * 5;
            var shade = 1 - i / 40.0;
            c.Rect(0, y, 640, 3, ((byte)(0x2E * shade + 20), (byte)(0x6B * shade + 24), (byte)(0x8F * shade + 30)));
        }
        for (var i = 0; i < 40; i++)
        {
            var x = (i * 89) % 620;
            var y = 208 + (i * 17) % 130;
            c.Blend(x, y, 0xFF, 0xC9, 0x8A, 0.5);
        }
    }

    private static void Mushroom(Canvas c)
    {
        c.Sky((0x11, 0x1B, 0x2E), (0x26, 0x36, 0x50));
        c.Stars(180, 0.55);
        c.Disc(500, 56, 18, (0xE8, 0xE8, 0xD4), 0.9);
        c.Hill(180, 226, 200, 50, (0x23, 0x2E, 0x28), 1);
        c.Ground(222, (0x2E, 0x3E, 0x2C), (0x27, 0x35, 0x26));
        c.Rect(150, 244, 10, 56, (0xD8, 0xD2, 0xBE));
        c.Rect(120, 226, 70, 22, (0xC0, 0x39, 0x2B));
        c.Rect(130, 220, 50, 8, (0xC0, 0x39, 0x2B));
        c.Rect(130, 234, 8, 6, (0xF2, 0xEC, 0xDC));
        c.Rect(160, 228, 8, 6, (0xF2, 0xEC, 0xDC));
        c.Rect(470, 268, 9, 40, (0xD8, 0xD2, 0xBE));
        c.Rect(446, 252, 58, 18, (0xC0, 0x39, 0x2B));
        c.Rect(456, 260, 7, 5, (0xF2, 0xEC, 0xDC));
    }

    private static void Village(Canvas c)
    {
        c.Sky((0x77, 0xAE, 0xE0), (0xD9, 0xE8, 0xF2));
        c.Cloud(90, 44, 8, 0.7);
        c.Cloud(420, 62, 7, 0.6);
        c.Hill(320, 206, 300, 36, (0x8F, 0xAE, 0x74), 0.85);
        c.Ground(204, (0xC8, 0xB0, 0x54), (0xB7, 0x9C, 0x44));
        // 简易房屋
        c.Rect(120, 190, 96, 60, (0x9A, 0x6B, 0x43));
        c.Rect(120, 178, 96, 14, (0x7A, 0x4A, 0x2E));
        c.Rect(150, 214, 22, 36, (0x6B, 0x4A, 0x2E));
        c.Rect(196, 206, 16, 16, (0x8E, 0xC8, 0xE8));
        // 麦田
        for (var x = 260; x < 620; x += 10)
            c.Rect(x, 214, 5, 26, (0xD8, 0xC0, 0x4A));
        c.Rect(380, 170, 10, 44, (0x6B, 0x4A, 0x2E));
        c.RectAlpha(352, 148, 66, 30, (0x3E, 0x7A, 0x2E), 1);
    }

    private static void Cave(Canvas c)
    {
        c.Sky((0x10, 0x12, 0x16), (0x1E, 0x22, 0x28));
        c.Ground(0, (0x2A, 0x2C, 0x30), (0x22, 0x24, 0x28), 12);
        // 矿洞轮廓
        c.Hill(90, 360, 240, 300, (0x1A, 0x1C, 0x20), 1);
        c.Hill(560, 360, 260, 260, (0x16, 0x18, 0x1C), 1);
        // 萤石
        foreach (var (x, y) in new[] { (180, 120), (300, 90), (430, 150), (520, 100), (250, 200), (400, 240) })
        {
            c.Disc(x, y, 16, (0xE8, 0xD0, 0x6A), 0.35);
            c.Rect(x - 6, y - 6, 12, 12, (0xF2, 0xDF, 0x8A));
            c.Rect(x - 4, y - 4, 8, 8, (0xFF, 0xF2, 0xB0));
        }
        // 铁矿石点缀
        c.Rect(220, 280, 10, 10, (0xC8, 0xB8, 0xA0));
        c.Rect(340, 310, 8, 8, (0xC8, 0xB8, 0xA0));
    }
}
