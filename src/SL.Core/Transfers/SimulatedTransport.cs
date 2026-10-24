using System.Security.Cryptography;

namespace SL.Core.Transfers;

/// <summary>
/// 阶段一的演示传输:按指定速率产生数据流,支持从断点(ResumeBytes)继续,
/// 并确定性地注入偶发失败以演练「单源重试 → 多源回退 → 最终成功」链路。
/// 失败注入规则:同一来源的最后一次尝试必定成功 ⇒ 任务最终必然完成。
/// </summary>
public sealed class SimulatedTransport : ITransport
{
    public async Task<TransportResult> TransferAsync(TransferContext context, CancellationToken cancellation)
    {
        var task = context.Task;
        long total = task.Spec.BytesTotal;
        if (total <= 0)
        {
            // 未给出总量时按 Id 哈希在 8–48MB 间取定值,保证多次续传一致
            total = 8_000_000 + (Math.Abs((int)PseudoHash(task.Id)) % 40_000_000);
            total -= total % 1000;
        }

        long position = Math.Clamp(context.ResumeBytes, 0, total);
        double speed = task.Spec.SimulatedSpeed <= 0 ? 1_500_000 : task.Spec.SimulatedSpeed;
        speed *= 0.92 + pseudoRandom(context.SourceUrl) * 0.16; // ±8% 抖动

        // 确定性失败注入:非最后一次尝试时约 20% 概率失败
        bool injectFailure =
            context.AttemptNumber < 3
            && PseudoHash(task.Id + "|" + context.SourceUrl + "|" + context.AttemptNumber) % 100 < 20;

        long partial = Math.Max(total / 7, 256_000);

        if (injectFailure)
        {
            // 稍作进度后失败,便于在 UI 观察到来源切换
            await Simulate(position, Math.Min(position + partial, total), speed, context, cancellation).ConfigureAwait(false);
            return TransportResult.Fail("演示:模拟来源超时,建议切换镜像");
        }

        await Simulate(position, total, speed, context, cancellation).ConfigureAwait(false);
        return TransportResult.Ok(total);
    }

    private static async Task Simulate(long from, long to, double speed, TransferContext context, CancellationToken ct)
    {
        if (to <= from)
            return;

        context.Progress(from);

        const int chunk = 64_000;
        long position = from;
        while (position < to)
        {
            ct.ThrowIfCancellationRequested();
            var take = Math.Min(chunk, to - position);
            position += take;
            context.Progress(Math.Min(position, to));

            var ms = take * 1000.0 / Math.Max(speed, 50_000);
            if (ms >= 1)
                await Task.Delay((int)Math.Min(ms, 120), ct).ConfigureAwait(false);
        }
    }

    private static long PseudoHash(string s)
    {
        long h = 17;
        foreach (var ch in s)
            h = h * 31 + ch;
        return h;
    }

    private static double pseudoRandom(string s) => PseudoHash(s) % 1000 / 1000.0;
}
