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
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace SL.Core.Launch;

/// <summary>
/// Asset object backfill: read assets/indexes/<id>.json and fill in the missing objects
/// (assets/objects/<first two chars of the hash>/<hash>) with 8-way concurrency, verifying SHA-1 on the fly.
/// Runs in the background, outside the task center (Pres aggregates it into one summary).
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

    /// <summary>Backfill progress (throttling is left to the subscriber).</summary>
    public static event Action<AssetProgress>? Progress;

    /// <summary>Returns the number of missing objects; 0 when everything is ready. A missing/corrupt asset index throws.</summary>
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
                        missing.Add(item); // size mismatch → download again
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

        // 8-way chunked concurrency
        const int lanes = 8;
        var tasks = new List<Task>();
        var chunks = missingRaw.Select((o, i) => (o, idx: i))
            .GroupBy(x => x.idx % lanes, x => x.o);
        tasks.AddRange(chunks.Select(worker));
        await Task.WhenAll(tasks).ConfigureAwait(false);

        // retry once on the mirror after a failure (sequential)
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

            // SHA-1 re-check (the asset object name is its hash)
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
