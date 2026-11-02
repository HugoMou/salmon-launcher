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
using System.Net.Http.Headers;

namespace SL.Core.Transfers;

/// <summary>
/// Stage 2 real HTTP transport: direct-to-disk writes + Range resume + integrity guards.
///
/// - Resume: prefer the ResumeBytes given by the engine; when the engine restarts from 0, consult the local prefix file
///   (pause/resume and cross-process resume are all covered uniformly here);
/// - When the server does not support Range (returns 200 instead of 206), degrade to rewriting the whole file;
/// - Apply a length guard after writing (compare with Content-Length / Spec.BytesTotal); the final SHA-1 check is run by the engine;
/// - Byte-exact: automatic decompression is disabled so gzip cannot rewrite the real byte count and break resume/verification.
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

        // a usable local file already exists (reinstall / previous completion) and the size matches (or SHA-1 is available for verification):
        // send no request and hand it to the engine for final verification; otherwise an oversized Range would trigger a 416 from the server.
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

        // Range out of bounds: the local prefix is larger than the server file, so delete it and re-download
        if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            StartOver(dest);
            return TransportResult.Fail($"HTTP 416 断点失效,已清空本地缓存重试");
        }

        // the server ignores Range and returns a full 200 → the prefix is invalid, rewrite the whole file
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
            throw; // the engine wraps up uniformly (pause keeps the resume point / cancel keeps it too)
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
            // if it cannot be deleted, leave it to the next round to overwrite
        }
    }

    /// <summary>Read the local prefix: returns the resume start offset; -1 means the local state is inconsistent (cleaned up, start from scratch).</summary>
    private static long PrepareLocalPrefix(TransferContext context, string dest)
    {
        if (!File.Exists(dest))
            return 0;

        var prefixLength = new FileInfo(dest).Length;
        var expectedTotal = context.Task.Spec.BytesTotal;

        if (expectedTotal <= 0 || prefixLength <= expectedTotal)
            return prefixLength; // partial prefix: Range resume (BytesReceived is corrected by the Progress reports of the engine)

        // prefix larger than expected → the source is untrustworthy, delete it and start over
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
