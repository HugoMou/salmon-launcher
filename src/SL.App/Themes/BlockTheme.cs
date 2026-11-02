// SalmonLauncher (SL) — a Minecraft Java edition launcher
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
using System.IO.Compression;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace SL.App.Themes;

/// <summary>A selectable Minecraft block texture.</summary>
public sealed record BlockSource(string Id, string Name, string File, string Hint);

/// <summary>A color scheme extracted from a block texture.</summary>
public sealed record BlockThemeResult(string DominantHex, ThemePalette Palette, AccentChoice Accent);

/// <summary>
/// Theme workshop: fetch block textures from the Minecraft client jar or online asset mirrors,
/// then derive a full UI color scheme (dark + light) from the dominant color and hand it to ThemeManager to apply.
/// </summary>
public static class BlockTheme
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>Online asset mirrors (organized by version directory; the first available one is used).</summary>
    private static readonly string[] Mirrors =
    [
        "https://raw.githubusercontent.com/InventivetalentDev/minecraft-assets/1.21.4/assets/minecraft/textures/block/{0}",
        "https://raw.githubusercontent.com/InventivetalentDev/minecraft-assets/1.20.4/assets/minecraft/textures/block/{0}",
        "https://raw.githubusercontent.com/InventivetalentDev/minecraft-assets/1.19.4/assets/minecraft/textures/block/{0}",
    ];

    /// <summary>List of blocks available for color sampling.</summary>
    public static IReadOnlyList<BlockSource> Blocks { get; } =
    [
        new("grass", "草方块", "grass_block_top.png", "地表绿"),
        new("stone", "石头", "stone.png", "中性灰"),
        new("oak-planks", "橡木木板", "oak_planks.png", "暖木色"),
        new("sand", "沙子", "sand.png", "沙漠米黄"),
        new("diamond", "钻石矿", "diamond_ore.png", "矿石青蓝"),
        new("gold", "金矿", "gold_ore.png", "金属金"),
        new("netherrack", "下界岩", "netherrack.png", "下界暗红"),
        new("end-stone", "末地石", "end_stone.png", "末地浅黄"),
        new("water", "水", "water_still.png", "海洋蓝"),
        new("lava", "岩浆", "lava_still.png", "熔岩橙红"),
        new("snow", "雪块", "snow.png", "冰雪白"),
        new("obsidian", "黑曜石", "obsidian.png", "深紫黑"),
        new("crimson", "绯红木板", "crimson_planks.png", "下界绯红"),
        new("warped", "诡异木板", "warped_planks.png", "下界青绿"),
        new("amethyst", "紫水晶块", "amethyst_block.png", "紫水晶"),
        new("copper", "铜块", "copper_block.png", "氧化铜橙"),
    ];

    /// <summary>Texture cache directory.</summary>
    private static string CacheDir
    {
        get
        {
            var dir = Path.Combine(Core.Boot.Runtime.ImageCacheDir, "blocks");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>
    /// Obtain the texture file (preferring the original texture from an installed client, falling back to online mirrors when none is installed).
    /// Returns the local png path; returns null if all attempts fail.
    /// </summary>
    public static async Task<string?> EnsureTextureAsync(BlockSource block, CancellationToken ct = default)
    {
        var dest = Path.Combine(CacheDir, block.File);
        if (File.Exists(dest) && new FileInfo(dest).Length > 1024)
            return dest;

        // 1) Local client jar (immediate and available offline)
        if (TryExtractFromJar(block.File, dest))
            return dest;

        // 2) Online mirrors
        foreach (var mirror in Mirrors)
        {
            var url = string.Format(mirror, block.File);
            try
            {
                await using var stream = await Http.GetStreamAsync(url, ct).ConfigureAwait(false);
                await using var file = File.Create(dest);
                await stream.CopyToAsync(file, ct).ConfigureAwait(false);
                if (new FileInfo(dest).Length > 1024)
                    return dest;
                File.Delete(dest);
            }
            catch
            {
                // Try the next mirror
            }
        }

        return null;
    }

    private static bool TryExtractFromJar(string file, string dest)
    {
        foreach (var jar in EnumerateClientJars())
        {
            try
            {
                using var archive = ZipFile.OpenRead(jar);
                var entry = archive.GetEntry($"assets/minecraft/textures/block/{file}");
                if (entry is null)
                    continue;
                entry.ExtractToFile(dest, overwrite: true);
                return true;
            }
            catch
            {
                // Ignore corrupted jars
            }
        }
        return false;
    }

    private static IEnumerable<string> EnumerateClientJars()
    {
        var root = Core.Boot.Runtime.InstancesDir;
        if (!Directory.Exists(root))
            yield break;

        foreach (var instance in Directory.EnumerateDirectories(root))
        {
            var versions = Path.Combine(instance, "versions");
            if (!Directory.Exists(versions))
                continue;
            foreach (var jar in Directory.EnumerateFiles(versions, "*.jar"))
                yield return jar;
        }
    }

    /// <summary>Read the texture's dominant color (average color of opaque pixels).</summary>
    public static async Task<(byte R, byte G, byte B)?> AverageColorAsync(string path)
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
            var data = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            var pixels = data.DetachPixelData();
            long r = 0, g = 0, b = 0, n = 0;
            for (var i = 0; i + 3 < pixels.Length; i += 4)
            {
                var a = pixels[i + 3];
                if (a < 160)
                    continue;
                b += pixels[i];
                g += pixels[i + 1];
                r += pixels[i + 2];
                n++;
            }
            if (n == 0)
                return null;
            return ((byte)(r / n), (byte)(g / n), (byte)(b / n));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Generate a full theme from the block's dominant color (dark/light palettes + accent).</summary>
    public static BlockThemeResult Build(string blockId, string blockName, byte r, byte g, byte b)
    {
        var (h, s, _) = ToHsl(r, g, b);
        var sat = Math.Clamp(s, 0.05, 0.45);

        var dark = new Dictionary<string, string>
        {
            ["Brush.PageBg"] = Hsl(h, sat * 0.58, 0.072),
            ["Brush.PanelBg"] = Hsl(h, sat * 0.58, 0.052),
            ["Brush.Card"] = Hsl(h, sat * 0.52, 0.130),
            ["Brush.CardHover"] = Hsl(h, sat * 0.52, 0.172),
            ["Brush.CardPress"] = Hsl(h, sat * 0.52, 0.108),
            ["Brush.Border"] = Hsl(h, sat * 0.48, 0.205),
            ["Brush.BorderStrong"] = Hsl(h, sat * 0.48, 0.290),
            ["Brush.Fg"] = Hsl(h, 0.07, 0.930),
            ["Brush.FgDim"] = Hsl(h, 0.09, 0.680),
            ["Brush.FgFaint"] = Hsl(h, 0.09, 0.500),
        };

        var light = new Dictionary<string, string>
        {
            ["Brush.PageBg"] = Hsl(h, sat * 0.34, 0.950),
            ["Brush.PanelBg"] = Hsl(h, sat * 0.34, 0.912),
            ["Brush.Card"] = Hsl(h, sat * 0.10, 1.000),
            ["Brush.CardHover"] = Hsl(h, sat * 0.22, 0.972),
            ["Brush.CardPress"] = Hsl(h, sat * 0.26, 0.940),
            ["Brush.Border"] = Hsl(h, sat * 0.30, 0.862),
            ["Brush.BorderStrong"] = Hsl(h, sat * 0.30, 0.740),
            ["Brush.Fg"] = Hsl(h, sat * 0.45, 0.140),
            ["Brush.FgDim"] = Hsl(h, sat * 0.30, 0.400),
            ["Brush.FgFaint"] = Hsl(h, sat * 0.24, 0.580),
        };

        var accentSat = Math.Clamp(s, 0.52, 0.92);
        var accentDark = Hsl(h, accentSat, 0.58);
        var accentLight = Hsl(h, accentSat, 0.42);

        var palette = new ThemePalette(
            $"mc-{blockId}",
            $"MC · {blockName}",
            "由方块纹理取色生成",
            dark,
            light);

        var accent = new AccentChoice($"mc-{blockId}", $"MC · {blockName}", accentDark, accentLight);

        return new BlockThemeResult(Hsl(h, s, 0.55), palette, accent);
    }

    // ---------------------------------------------------------- Color conversion

    private static (double H, double S, double L) ToHsl(byte r, byte g, byte b)
    {
        var rf = r / 255.0;
        var gf = g / 255.0;
        var bf = b / 255.0;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var l = (max + min) / 2;
        if (Math.Abs(max - min) < 0.0001)
            return (210, 0, l);

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (Math.Abs(max - rf) < 0.0001)
            h = ((gf - bf) / d + (gf < bf ? 6 : 0)) / 6;
        else if (Math.Abs(max - gf) < 0.0001)
            h = ((bf - rf) / d + 2) / 6;
        else
            h = ((rf - gf) / d + 4) / 6;
        return (h * 360, s, l);
    }

    private static string Hsl(double h, double s, double l)
    {
        var hh = ((h % 360) + 360) % 360 / 360;
        s = Math.Clamp(s, 0, 1);
        l = Math.Clamp(l, 0, 1);

        double r, g, b;
        if (s < 0.0001)
        {
            r = g = b = l;
        }
        else
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            r = HueToRgb(p, q, hh + 1.0 / 3);
            g = HueToRgb(p, q, hh);
            b = HueToRgb(p, q, hh - 1.0 / 3);
        }
        return $"#{ToByte(r):X2}{ToByte(g):X2}{ToByte(b):X2}";
    }

    private static double HueToRgb(double p, double q, double t)
    {
        if (t < 0)
            t += 1;
        if (t > 1)
            t -= 1;
        if (t < 1.0 / 6)
            return p + (q - p) * 6 * t;
        if (t < 1.0 / 2)
            return q;
        if (t < 2.0 / 3)
            return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private static byte ToByte(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
}
