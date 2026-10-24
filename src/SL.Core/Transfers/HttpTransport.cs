using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace SL.Core.Transfers;

/// <summary>
/// 阶段二的真实 HTTP 传输:直接落盘 + Range 断点续传 + 完整性护栏。
///
/// - 断点续传:优先复用引擎给定的 ResumeBytes;引擎从 0 重新起算时,参考本地已有前缀文件
///   (暂停 / 恢复、跨进程重启的续传都由此统一兜住);
/// - 服务器不支持 Range(返回 200 而非 206)时自动退化为整文件重写;
/// - 写完做长度守卫(与 Content-Length / Spec.BytesTotal 比对),SHA-1 终验由引擎统一执行;
/// - 字节级精确:禁用自动解压,避免 gzip 改写实际字节数破坏续传与校验。
/// </summary>
public sealed class HttpTransport : ITransport
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };

        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(100) };
        var ua = client.DefaultRequestHeaders.UserAgent;
        ua.Add(new ProductInfoHeaderValue("SalmonLauncher", Core.Boot.Runtime.AppVersion.Split([' '], 2)[0]));
        ua.Add(new ProductInfoHeaderValue("(Windows)"));
        return client;
    }

    public async Task<TransportResult> TransferAsync(TransferContext context, CancellationToken cancellation)
    {
        var dest = context.Task.Spec.DestPath;
        if (string.IsNullOrWhiteSpace(dest))
            return TransportResult.Fail("Http 任务缺少落盘路径(DestPath)");

        var dir = Path.GetDirectoryName(dest);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // 本地已有可用文件(重复安装 / 上次已完成)且大小吻合(或带 SHA-1 可校验):
        // 不再发请求,直接交给引擎做终验;否则会带超大 Range 触发服务器 416。
        var expectedTotal = context.Task.Spec.BytesTotal;
        if (File.Exists(dest) && new FileInfo(dest).Length > 0)
        {
            var localLength = new FileInfo(dest).Length;
            var sizeMatches = expectedTotal <= 0 || localLength == expectedTotal;
            var verifiable = expectedTotal > 0 || !string.IsNullOrEmpty(context.Task.Spec.ExpectedSha1);
            if (sizeMatches && verifiable)
            {
                context.Progress(localLength);
                return TransportResult.Ok(localLength);
            }
        }

        var startBytes = PrepareLocalPrefix(context, dest);
        if (startBytes < 0)
            return TransportResult.Fail("本地断点文件异常,已重新排队");

        using var request = new HttpRequestMessage(HttpMethod.Get, context.SourceUrl);
        if (startBytes > 0)
            request.Headers.Range = new RangeHeaderValue(startBytes, null);

        using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation)
            .ConfigureAwait(false);

        // Range 越界:本地前缀比服务器文件还大,删掉重下
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            StartOver(dest);
            return TransportResult.Fail($"HTTP 416 断点失效,已清空本地缓存重试");
        }

        // 服务器无视 Range 返回完整 200 → 前缀失效,整文件重写
        if (response.StatusCode == HttpStatusCode.OK)
            startBytes = 0;
        else if (response.StatusCode != HttpStatusCode.PartialContent
                 && response.StatusCode != HttpStatusCode.OK)
            return TransportResult.Fail($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        var contentLength = response.Content.Headers.ContentLength ?? -1;
        var total = response.StatusCode == HttpStatusCode.PartialContent && contentLength > 0
            ? startBytes + contentLength
            : contentLength;
        if (context.Task.Spec.BytesTotal > 0 && total < 0)
            total = context.Task.Spec.BytesTotal;

        try
        {
            await using var file = new FileStream(
                dest,
                startBytes > 0 ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.Read,
                1 << 16,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await using var stream = await response.Content
                .ReadAsStreamAsync(cancellation)
                .ConfigureAwait(false);

            var buffer = new byte[1 << 16];
            long offset = startBytes;
            while (true)
            {
                var read = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false);
                if (read == 0)
                    break;

                await file.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                offset += read;
                context.Progress(offset);
            }

            await file.FlushAsync(cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // 引擎统一收尾(暂停保留断点 / 取消保留断点)
        }

        var finalLength = new FileInfo(dest).Length;
        if (total > 0 && finalLength != total)
            return TransportResult.Fail($"下载不完整:期望 {totalLengthFmt(total)},实际 {totalLengthFmt(finalLength)}(网络中断)");

        context.Progress(finalLength);
        return TransportResult.Ok(finalLength);
    }

    private static void StartOver(string dest)
    {
        try
        {
            if (File.Exists(dest))
                File.Delete(dest);
        }
        catch (IOException)
        {
            // 删不掉就交给下一轮覆盖写
        }
    }

    /// <summary>读取本地前缀:返回续传起点;返回 -1 表示本地状态无法自洽(已清理,从头来)。</summary>
    private static long PrepareLocalPrefix(TransferContext context, string dest)
    {
        if (!File.Exists(dest))
            return 0;

        var prefixLength = new FileInfo(dest).Length;
        var expectedTotal = context.Task.Spec.BytesTotal;

        if (expectedTotal <= 0 || prefixLength <= expectedTotal)
            return prefixLength; // 部分前缀:Range 续传(引擎 BytesReceived 由 Progress 回报矫正)

        // 前缀比期望值还大 → 来源不可信,删除重来
        context.Progress(0);
        try
        {
            File.Delete(dest);
        }
        catch (IOException)
        {
            return -1;
        }
        return 0;
    }

    private static string totalLengthFmt(long bytes)
        => bytes >= 1 << 20 ? $"{bytes / (1024.0 * 1024.0):F1} MB" : $"{bytes / 1024.0:F0} KB";
}
