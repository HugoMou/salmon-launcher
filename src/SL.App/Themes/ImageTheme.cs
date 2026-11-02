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
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace SL.App.Themes;

/// <summary>A theme extracted from a wallpaper (palette + accent + wallpaper path).</summary>
public sealed record ImageThemeResult(string DominantHex, ThemePalette Palette, AccentChoice Accent);

/// <summary>Sample colors from an image and generate a theme: the dominant color determines the accent, the average color biases the background.</summary>
public static class ImageTheme
{
    /// <summary>Read image pixels (BGRA8).</summary>
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

    /// <summary>Average color (used to judge overall lightness).</summary>
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

    /// <summary>Dominant color: quantized into 16 levels, picking the color that appears often and is vivid enough.</summary>
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

            // Weight frequency first, with a mild preference for vivid colors of medium lightness
            var score = Math.Log(entry.Count + 1) * (0.35 + sat * 1.4) * (1 - Math.Abs(lum - 0.55) * 1.6);
            if (score > bestScore)
            {
                bestScore = score;
                best = (r, g, b);
            }
        }
        return best;
    }

    /// <summary>Generate a theme from an image (color derivation reuses the HSL logic from block themes).</summary>
    public static async Task<ImageThemeResult?> BuildAsync(string id, string name, string imagePath, string hint)
    {
        var pixels = await ReadPixelsAsync(imagePath);
        if (pixels is null)
            return null;

        var (r, g, b) = Dominant(pixels.Value.Pixels);
        var (ar, ag, ab) = Average(pixels.Value.Pixels);

        // 70% dominant color and 30% average color, so an extremely vivid image does not skew the UI
        var mr = (byte)((r * 0.7) + (ar * 0.3));
        var mg = (byte)((g * 0.7) + (ag * 0.3));
        var mb = (byte)((b * 0.7) + (ab * 0.3));

        var built = BlockTheme.Build(id, name, mr, mg, mb);
        var palette = built.Palette with { Hint = hint, Background = imagePath };
        var accent = built.Accent with { Name = name };
        return new ImageThemeResult($"#{r:X2}{g:X2}{b:X2}", palette, accent);
    }
}
