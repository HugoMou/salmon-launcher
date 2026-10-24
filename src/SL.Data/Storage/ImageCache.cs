using System.Security.Cryptography;
using System.Text;

namespace SL.Data.Storage;

/// <summary>
/// 网络图片的本地文件缓存,按 URL 哈希组织文件名。
/// 由业务层封装后供 MyImage 等控件使用。
/// </summary>
public sealed class ImageCache
{
    private readonly string _dir;

    public ImageCache(string cacheDir)
    {
        _dir = cacheDir;
        Directory.CreateDirectory(_dir);
    }

    public static string KeyFor(string url)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(url));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public string PathFor(string url) => Path.Combine(_dir, KeyFor(url) + ".img");

    public byte[]? TryLoad(string url)
    {
        try
        {
            var path = PathFor(url);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch
        {
            return null;
        }
    }

    public void Store(string url, byte[] data)
    {
        try
        {
            var key = KeyFor(url);
            var tmp = Path.Combine(_dir, key + ".tmp");
            File.WriteAllBytes(tmp, data);
            File.Move(tmp, Path.Combine(_dir, key + ".img"), overwrite: true);
        }
        catch
        {
            // 缓存写入失败不致命:下次仍会走网络。
        }
    }
}
