using System.IO;
using System.Net.Http;

namespace SL.Core.Skins;

/// <summary>一个已保存的皮肤。</summary>
/// <param name="OwnerId">所属账户 Id(msa:&lt;uuid&gt; / offline:&lt;名字&gt;)</param>
/// <param name="FileName">文件名(含扩展名)</param>
/// <param name="Path">完整路径</param>
/// <param name="Name">显示名(去扩展名)</param>
/// <param name="Width">像素宽(64 / 128)</param>
/// <param name="Height">像素高(64 为现代皮肤,32 为旧版)</param>
/// <param name="SavedAt">保存时间</param>
public sealed record SkinItem(string OwnerId, string FileName, string Path, string Name,
    int Width, int Height, DateTimeOffset SavedAt)
{
    public string SizeText => $"{Width}×{Height}";
    public string ModelText => Height >= 64 ? "现代皮肤(可含外层)" : "旧版皮肤";
    public string SavedText => SavedAt.ToString("yyyy-MM-dd HH:mm");
    public bool IsSlim => Height >= 64;
}

/// <summary>
/// 皮肤仓库:按账户分目录保存皮肤文件(数据目录 skins/&lt;ownerId&gt;/),
/// 支持从本地导入与按 URL 下载;文件即事实,便于用户自行管理。
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

    /// <summary>从本地文件导入(复制;同名自动追加序号)。返回落盘文件名。</summary>
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

    /// <summary>按 URL 下载皮肤(仅 http/https),返回落盘文件名。</summary>
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

    /// <summary>读 PNG 头部 IHDR 取宽高(不依赖图像解码)。</summary>
    private static (int Width, int Height) ReadPngSize(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var header = new byte[26];
            if (stream.Read(header, 0, header.Length) < header.Length)
                return (0, 0);
            // IHDR 数据在 16..24,大端序
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