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
using System.IO;
using System.Net.Http;

namespace SL.Core.Skins;

/// <summary>A saved skin.</summary>
/// <param name="OwnerId">Owning account Id (msa:&lt;uuid&gt; / offline:&lt;name&gt;)</param>
/// <param name="FileName">File name (including the extension)</param>
/// <param name="Path">Full path</param>
/// <param name="Name">Display name (extension removed)</param>
/// <param name="Width">Pixel width (64 / 128)</param>
/// <param name="Height">Pixel height (64 for modern skins, 32 for legacy)</param>
/// <param name="SavedAt">Save time</param>
public sealed record SkinItem(string OwnerId, string FileName, string Path, string Name,
    int Width, int Height, DateTimeOffset SavedAt)
{
    public string SizeText => $"{Width}×{Height}";
    public string ModelText => Height >= 64 ? "现代皮肤(可含外层)" : "旧版皮肤";
    public string SavedText => SavedAt.ToString("yyyy-MM-dd HH:mm");
    public bool IsSlim => Height >= 64;
}

/// <summary>
/// Skin repository: skin files are stored per account directory (data directory skins/&lt;ownerId&gt;/),
/// with local import and URL download; files are the source of truth, so users can manage them directly.
/// </summary>
public static class SkinLibrary
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(30) };

    public static string DirOf(string ownerId)
        => Path.Combine(Boot.Runtime.Root, "skins", Sanitize(ownerId));

    public static IReadOnlyList<SkinItem> List(string ownerId)
    {
        var dir = DirOf(ownerId);
        var list = new List<SkinItem>();
        if (!Directory.Exists(dir))
            return list;

        foreach (var path in Directory.EnumerateFiles(dir, "*.png"))
        {
            var (w, h) = ReadPngSize(path);
            var info = new FileInfo(path);
            list.Add(new SkinItem(ownerId, info.Name, path,
                Path.GetFileNameWithoutExtension(info.Name), w, h,
                new DateTimeOffset(info.LastWriteTime)));
        }
        return list.OrderByDescending(s => s.SavedAt).ToList();
    }

    /// <summary>Import from a local file (copy; a duplicate name gets a sequence number appended). Returns the file name written.</summary>
    public static string Import(string ownerId, string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("找不到文件:" + sourcePath);
        if (!sourcePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("皮肤必须是 PNG 文件");

        var dir = DirOf(ownerId);
        Directory.CreateDirectory(dir);
        var name = Path.GetFileName(sourcePath);
        var target = Path.Combine(dir, name);
        if (File.Exists(target))
        {
            var stem = Path.GetFileNameWithoutExtension(name);
            for (var i = 2; File.Exists(target); i++)
                target = Path.Combine(dir, $"{stem} ({i}).png");
        }
        File.Copy(sourcePath, target);
        return Path.GetFileName(target);
    }

    /// <summary>Download a skin by URL (http/https only); returns the file name written.</summary>
    public static async Task<string> DownloadAsync(string ownerId, string url, string? name,
        CancellationToken cancellation = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("只支持 http/https 皮肤地址");

        var bytes = await Client.GetByteArrayAsync(uri, cancellation).ConfigureAwait(false);
        if (bytes.Length < 100)
            throw new InvalidOperationException("下载内容过小,不像是 PNG 皮肤");

        var dir = DirOf(ownerId);
        Directory.CreateDirectory(dir);

        var stem = string.IsNullOrWhiteSpace(name) ? uri.Segments.LastOrDefault() ?? "skin.png" : name!;
        stem = Path.GetFileNameWithoutExtension(stem);
        if (stem.Length == 0)
            stem = "skin";

        var target = Path.Combine(dir, stem + ".png");
        for (var i = 2; File.Exists(target); i++)
            target = Path.Combine(dir, $"{stem} ({i}).png");

        await File.WriteAllBytesAsync(target, bytes, cancellation).ConfigureAwait(false);
        return Path.GetFileName(target);
    }

    public static void Delete(string ownerId, string fileName)
    {
        var path = Path.Combine(DirOf(ownerId), fileName);
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>Read width/height from the PNG IHDR header (does not rely on image decoding).</summary>
    private static (int Width, int Height) ReadPngSize(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var header = new byte[26];
            if (stream.Read(header, 0, header.Length) < header.Length)
                return (0, 0);
            // IHDR data sits at 16..24, big-endian
            var width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            var height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            return (width, height);
        }
        catch (IOException)
        {
            return (0, 0);
        }
    }

    private static string Sanitize(string ownerId)
    {
        var safe = ownerId.Replace(':', '_');
        foreach (var c in Path.GetInvalidFileNameChars())
            safe = safe.Replace(c, '_');
        return safe.Length == 0 ? "default" : safe;
    }
}