using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SL.Core.Launch;

/// <summary>
/// Java 运行时确保:本机扫描满足要求 → 直接用;
/// 不满足 → 从 Adoptium 下载对应主版本的 JDK(显示字节进度),解压到 java\ 后返回。
/// </summary>
public static class JavaFetch
{
    /// <summary>下载进度(字节)。Pres 节流后展示。</summary>
    public static event Action<long, long>? DownloadProgress;

    public static async Task<string?> EnsureJavaAsync(int minMajor, CancellationToken cancellation)
    {
        // 1. 本机已有
        var list = await SL.Core.Java.JavaScan.ScanAsync(cancellation).ConfigureAwait(false);
        var usable = list
            .Where(j => j.MajorVersion >= minMajor)
            .OrderByDescending(j => j.MajorVersion)
            .ToList();
        if (usable.Count > 0)
            return usable[0].HomePath;

        // 2. 下载 Temurin JDK(Adoptium API)
        return await DownloadTemurinAsync(minMajor, cancellation).ConfigureAwait(false);
    }

    private static string JavaRoot => Path.Combine(Boot.Runtime.Root, "java");

    private static async Task<string?> DownloadTemurinAsync(int major, CancellationToken cancellation)
    {
        Directory.CreateDirectory(JavaRoot);
        var marker = Path.Combine(JavaRoot, $"temurin-{major}", "release");
        if (File.Exists(marker))
        {
            var exe = Path.Combine(JavaRoot, $"temurin-{major}", "bin", "java.exe");
            if (File.Exists(exe))
                return Path.Combine(JavaRoot, $"temurin-{major}");
        }

        // API:https://api.adoptium.net/v3/assets/latest/<major>/hotspot
        const string api = "https://api.adoptium.net/v3/assets/latest/{0}/hotspot?architecture=x64&image_type=jdk&os=windows&vendor=eclipse";
        string json;
        using (var apiClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
        {
            json = await apiClient.GetStringAsync(string.Format(api, major), cancellation).ConfigureAwait(false);
        }

        // 取第一个 jdk 二进制包(zip 的 link / sha256)
        string? link = null, checksum = null, fileName = null;
        using (var doc = System.Text.Json.JsonDocument.Parse(json))
        {
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("binary", out var binary))
                    continue;
                if (!binary.TryGetProperty("package", out var pkg))
                    continue;
                link = pkg.TryGetProperty("link", out var l) ? l.GetString() : null;
                checksum = pkg.TryGetProperty("checksum", out var c) ? c.GetString() : null;
                fileName = pkg.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (!string.IsNullOrEmpty(link))
                    break;
            }
        }

        if (string.IsNullOrEmpty(link) || string.IsNullOrEmpty(fileName))
            throw new InvalidOperationException("Adoptium API 未返回可用的 JDK 包");

        var zipTarget = Path.Combine(JavaRoot, fileName);
        var bytesOnDisk = File.Exists(zipTarget) ? new FileInfo(zipTarget).Length : 0;

        // 下载:强制 HTTP/1.1(HTTP/2 对 GitHub 大文件存在挂流风险),看门狗 + 断点续传 + 重试
        using var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(20),
        };
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SalmonLauncher/0.3");

        long total = -1;
        try
        {
            using var head = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Head, link);
            using var probe = await client.SendAsync(head, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellation)
                .ConfigureAwait(false);
            total = probe.Content.Headers.ContentLength ?? -1;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception)
        {
            total = -1; // HEAD 失败不致命
        }

        const int maxAttempts = 4;

        async Task TryDownloadAsync(HttpClient dlClient, string url, string label, long expectedTotal)
        {
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                var bytesOnDisk0 = File.Exists(zipTarget) ? new FileInfo(zipTarget).Length : 0;
                if (expectedTotal > 0 && bytesOnDisk0 >= expectedTotal)
                    return;

                Core.Logging.AppLog.Info("Java", $"JDK [{label}] 尝试 {attempt}/{maxAttempts}(已有 {bytesOnDisk0} 字节)");
                var attemptCts = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                try
                {
                    using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url)
                    {
                        // 强制 HTTP/1.1:HTTP/2 对 GitHub 大文件偶发挂流
                        Version = System.Net.HttpVersion.Version11,
                        VersionPolicy = System.Net.Http.HttpVersionPolicy.RequestVersionExact,
                    };
                    if (bytesOnDisk0 > 0)
                        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(bytesOnDisk0, null);

                    using var response = await dlClient
                        .SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, attemptCts.Token)
                        .ConfigureAwait(false);
                    if (response.StatusCode is not (System.Net.HttpStatusCode.PartialContent or System.Net.HttpStatusCode.OK))
                        throw new InvalidOperationException($"JDK 下载失败:HTTP {(int)response.StatusCode}");

                    var resume = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
                    var mode = resume ? FileMode.Append : FileMode.Create;
                    var appended = resume ? bytesOnDisk0 : 0;
                    if (!resume && bytesOnDisk0 > 0)
                        Core.Logging.AppLog.Info("Java", "服务器不支持 Range,从零重下");

                    await using var file = new FileStream(zipTarget, mode, FileAccess.Write, FileShare.Read, 1 << 18, FileOptions.Asynchronous);
                    await using var stream = await response.Content.ReadAsStreamAsync(attemptCts.Token).ConfigureAwait(false);
                    var buffer = new byte[1 << 18];
                    long received = appended;
                    var sinceRaise = 0L;
                    var total0 = response.Content.Headers.ContentLength is { } l ? l : -1;
                    var grandTotal = total0 > 0 ? appended + total0 : -1;
                    while (true)
                    {
                        // 看门狗:60s 无字节进度则放弃本次尝试(涓流由换源兜底)
                        var readTask = stream.ReadAsync(buffer, attemptCts.Token).AsTask();
                        var done = await Task.WhenAny(readTask, Task.Delay(TimeSpan.FromSeconds(60), attemptCts.Token))
                            .ConfigureAwait(false);
                        if (done != readTask)
                            throw new TimeoutException("JDK 流式下载 60s 无进展");
                        var read = await readTask.ConfigureAwait(false);
                        if (read == 0)
                            break;
                        await file.WriteAsync(buffer.AsMemory(0, read), attemptCts.Token).ConfigureAwait(false);
                        received += read;
                        sinceRaise += read;
                        if (sinceRaise >= 4 << 20 && grandTotal > 0)
                        {
                            sinceRaise = 0;
                            DownloadProgress?.Invoke(received, grandTotal);
                        }
                    }
                    await file.FlushAsync(attemptCts.Token).ConfigureAwait(false);
                    if (grandTotal > 0)
                        DownloadProgress?.Invoke(received, grandTotal);
                    Core.Logging.AppLog.Info("Java", $"JDK [{label}] 下载完成:{received} 字节");
                    return;
                }
                catch (Exception ex) when (ex is TimeoutException or IOException
                                           || (ex is System.Net.Http.HttpRequestException && cancellation.IsCancellationRequested is false))
                {
                    Core.Logging.AppLog.Error("Java", ex);
                    if (attempt == maxAttempts)
                        throw;
                }
                finally
                {
                    attemptCts.Cancel();
                    attemptCts.Dispose();
                }
            }
        }

        // 源顺序:TUNA 镜像(国内速度优) → Adoptium/GitHub 官方
        var sources = new List<(string Url, string Label)>
        {
            ($"https://mirrors.tuna.tsinghua.edu.cn/Adoptium/{major}/jdk/x64/windows/{fileName}", "TUNA 镜像"),
            (link, "Adoptium/GitHub"),
        };

        var downloaded = false;
        var lastError = (Exception?)null;
        foreach (var (sourceUrl, label) in sources)
        {
            try
            {
                Core.Logging.AppLog.Info("Java", $"JDK 源:{label}");
                await TryDownloadAsync(client, sourceUrl, label, total).ConfigureAwait(false);
                if (File.Exists(zipTarget) && new FileInfo(zipTarget).Length > 0)
                {
                    downloaded = true;
                    break;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastError = ex;
                Core.Logging.AppLog.Error("Java", ex);
            }
        }
        if (!downloaded)
            throw new InvalidOperationException("所有 JDK 源均下载失败", lastError);

        // SHA-256 整体校验(Adoptium 提供;SHA-1 不适用此处)
        var expectedFileSha = checksum;
        if (!string.IsNullOrEmpty(expectedFileSha))
        {
            using var fs = File.OpenRead(zipTarget);
            using var sha256 = SHA256.Create();
            var actual = Convert.ToHexString(await sha256.ComputeHashAsync(fs, cancellation).ConfigureAwait(false)).ToLowerInvariant();
            if (!string.Equals(actual, expectedFileSha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("JDK 包 SHA-256 校验失败,已中止");
        }

        // 解压(去掉 zip 内首个顶层目录)
        var extractDir = Path.Combine(JavaRoot, $"temurin-{major}");
        if (Directory.Exists(extractDir))
            Directory.Delete(extractDir, recursive: true);
        Directory.CreateDirectory(extractDir);
        System.IO.Compression.ZipFile.ExtractToDirectory(zipTarget, extractDir, overwriteFiles: true);

        // zip 里可能是 jdk-25.0.1+8 → 把子目录上提一层
        var javaExe = Path.Combine(extractDir, "bin", "java.exe");
        if (!File.Exists(javaExe))
        {
            var sub = Directory.EnumerateDirectories(extractDir).FirstOrDefault();
            if (sub is null)
                throw new InvalidOperationException("JDK 解压结构异常(bin/java.exe 缺失)");
            foreach (var entry in Directory.GetFileSystemEntries(sub))
                Directory.Move(entry, Path.Combine(extractDir, Path.GetFileName(entry)));
            Directory.Delete(sub, recursive: false);
            if (!File.Exists(Path.Combine(extractDir, "bin", "java.exe")))
                throw new InvalidOperationException("JDK 上提失败");
            // 上提后删除 zip 内已迁移的目录自身(替代:直接把 zip 保留也无妨)
        }

        return extractDir;
    }

    /// <summary>离线账户 UUID:UUID v3(MD5 of "OfflinePlayer:<name>")。</summary>
    public static string OfflineUuid(string name)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x30);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        var hex = Convert.ToHexString(bytes).ToLowerInvariant();
        return $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..32]}";
    }
}
