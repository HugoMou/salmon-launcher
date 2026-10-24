using System.Windows;
using SL.Core.Transfers;
using SL.Pres.Infrastructure;
using SL.Pres.Notify;

namespace SL.Pres;

/// <summary>
/// 演示任务工厂:阶段一的下载均为模拟数据流(SimulatedTransport),
/// 每个任务带主源 + 镜像源,以完整演练「重试→切换镜像→成功」链路。
/// 阶段二将在这里换成真实 Minecraft 文件清单与下载地址。
/// </summary>
public static class DemoTasks
{
    public static TransferSpec Make(string displayName, long bytesTotal, double speedBps, int priority = TransferPriority.Normal)
        => new()
        {
            DisplayName = displayName,
            Kind = TransferKind.Simulated,
            Priority = priority,
            BytesTotal = bytesTotal,
            SimulatedSpeed = speedBps,
            Sources =
            [
                new TransferSourceSpec($"https://dl.primary.example/{displayName}",
                    isMirror: false),
                new TransferSourceSpec($"https://mirror-a.example/{displayName}",
                    isMirror: true),
                new TransferSourceSpec($"https://mirror-b.example/{displayName}",
                    isMirror: true),
            ],
        };

    /// <summary>提交一个演示任务(随机大小 8–48MB,随机速率)。</summary>
    public static void SpawnSample(string displayName, int priority = TransferPriority.Normal)
    {
        var size = 8_000_000L + Random.Shared.Next(40_000_000);
        var speed = 600_000.0 + Random.Shared.NextDouble() * 1_600_000.0;
        AppServices.Transfers.Submit(Make(displayName, size, speed, priority));
        AppServices.Notify.Publish(NotifyKind.Info, $"已加入下载队列:{displayName}");
    }

    /// <summary>按「全面安装」文件计划提交一组任务(游戏核心 > 库 > 资源)。</summary>
    public static void SpawnInstallPlan()
    {
        SubmitPlanned("游戏核心 1.20.1", 22_943_000, 1_900_000, TransferPriority.CoreGameFile);
        SubmitPlanned("增量库文件包", 58_211_000, 1_400_000, TransferPriority.Library);
        SubmitPlanned("资源文件补丁", 128_904_000, 1_100_000, TransferPriority.Asset);
        SubmitPlanned("资源包索引", 14_300_000, 900_000, TransferPriority.Low);
        AppServices.Notify.Publish(NotifyKind.Success, "安装任务已提交,可在任务中心查看进度");
    }

    private static void SubmitPlanned(string name, long size, double speed, int priority)
        => AppServices.Transfers.Submit(Make(name, size, speed, priority));
}
