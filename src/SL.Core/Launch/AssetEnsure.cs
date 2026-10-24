using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace SL.Core.Launch;

/// <summary>
/// 资源对象补齐:读取 assets/indexes/<id>.json,把缺失的 objects
/// (assets/objects/<hash 前两位>/<hash>)按 8 并发补齐,SHA-1 随手复核。
/// 后台发生,不进任务中心(由 Pres 聚合为一条汇总)。
/// </summary>
public static class AssetEnsure
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            MaxConnectionsPerServer = 16,
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SalmonLauncher/0.3");
        return client;
    }

    public sealed record AssetObject(string Hash, long Size, string Url, string? MirrorUrl);

    public sealed record AssetProgress(int Done, int Total, long Bytes);

    /// <summary>补齐进度(节流由订阅方自行处理)。</summary>
    public static event Action<AssetProgress>? Progress;

    /// <summary>返回缺失数;全部就绪返回 0。资产索引文件缺失/损坏 → 抛出。</summary>
    public static async Task<int> EnsureAsync(string indexFile, string assetsRoot, CancellationToken cancellation)
    {
        var (objects, missingRaw) = await Task.Run(() =>
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(indexFile));
            var list = new List<AssetObject>(4096);
            var missing = new List<AssetObject>();
            if (doc.RootElement.TryGetProperty("objects", out var objects))
            {
                foreach (var obj in objects.EnumerateObject())
                {
                    var hash = obj.Value.GetProperty("hash").GetString() ?? "";
                    var size = obj.Value.TryGetProperty("size", out var sz) ? sz.GetInt64() : -1;
                    if (hash.Length < 2)
                        continue;
                    var item = new AssetObject(
                        hash, size,
                        $"https://resources.download.minecraft.net/{hash[..2]}/{hash}",
                        $"https://bmclapi2.bangbang93.com/assets/{hash[..2]}/{hash}");
                    list.Add(item);

                    var target = Path.Combine(assetsRoot, "objects", hash[..2], hash);
                    if (!File.Exists(target))
                        missing.Add(item);
                    else if (size >= 0 && new FileInfo(target).Length != size)
                        missing.Add(item); // 大小不符 → 重下
                }
            }
            return (list, missing);
        }, cancellation).ConfigureAwait(false);

        if (missingRaw.Count == 0)
            return 0;

        var done = 0;
        var failed = new List<AssetObject>();
        object gate = new();

        async Task worker(IEnumerable<AssetObject> items)
        {
            foreach (var obj in items)
            {
                cancellation.ThrowIfCancellationRequested();
                var ok = await DownloadOneAsync(obj, assetsRoot, cancellation).ConfigureAwait(false);
                if (!ok)
                    lock (gate) failed.Add(obj);
                var d = Interlocked.Increment(ref done);
                Progress?.Invoke(new AssetProgress(d, missingRaw.Count, 0));
            }
        }

        // 8 路切片并发
        const int lanes = 8;
        var tasks = new List<Task>();
        var chunks = missingRaw.Select((o, i) => (o, idx: i))
            .GroupBy(x => x.idx % lanes, x => x.o);
        tasks.AddRange(chunks.Select(worker));
        await Task.WhenAll(tasks).ConfigureAwait(false);

        // 失败一次镜像重试(顺序)
        foreach (var obj in failed)
        {
            cancellation.ThrowIfCancellationRequested();
            await DownloadMirrorAsync(obj, assetsRoot, cancellation).ConfigureAwait(false);
        }

        return missingRaw.Count;
    }

    private static async Task<bool> DownloadOneAsync(AssetObject obj, string assetsRoot, CancellationToken ct)
    {
        var target = Path.Combine(assetsRoot, "objects", obj.Hash[..2], obj.Hash);
        if (!await TryGetAsync(obj.Url, target, obj, ct).ConfigureAwait(false))
            return await TryGetAsync(obj.MirrorUrl ?? "", target, obj, ct).ConfigureAwait(false);
        return true;
    }

    private static async Task<bool> DownloadMirrorAsync(AssetObject obj, string assetsRoot, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(obj.MirrorUrl))
            return false;
        var target = Path.Combine(assetsRoot, "objects", obj.Hash[..2], obj.Hash);
        return await TryGetAsync(obj.MirrorUrl, target, obj, ct).ConfigureAwait(false);
    }

    private static async Task<bool> TryGetAsync(string url, string target, AssetObject obj, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var bytes = await Client.GetByteArrayAsync(url, ct).ConfigureAwait(false);
            if (obj.Size >= 0 && bytes.Length != obj.Size)
                return false;

            // SHA-1 复核(资产对象名即哈希)
            using var sha = System.Security.Cryptography.SHA1.Create();
            var actual = Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
            if (!string.Equals(actual, obj.Hash, StringComparison.OrdinalIgnoreCase))
                return false;

            await File.WriteAllBytesAsync(target, bytes, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
