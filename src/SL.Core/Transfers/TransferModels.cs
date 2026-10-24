namespace SL.Core.Transfers;

/// <summary>任务状态机:等待 → 下载中 →(校验中,阶段二)→ 完成;失败/暂停/取消为旁路。</summary>
public enum TransferState
{
    Queued = 0,
    Paused,
    Downloading,
    Verifying,
    Completed,
    Failed,
    Cancelled,
}

public enum TransferKind
{
    /// <summary>演示用模拟数据流(阶段一)</summary>
    Simulated = 0,

    /// <summary>真实 HTTP 下载(阶段二接入)</summary>
    Http,
}

/// <summary>多源列表中的一项:首位为主源,其后为镜像源(按历史成功率动态排序)。</summary>
public sealed class TransferSourceSpec
{
    public string Url { get; }
    public bool IsMirror { get; }

    public TransferSourceSpec(string url, bool isMirror = false)
    {
        Url = url;
        IsMirror = isMirror;
    }
}

/// <summary>提交给引擎的任务描述(不可变入参)。</summary>
public sealed class TransferSpec
{
    public required string DisplayName { get; init; }
    public TransferKind Kind { get; init; } = TransferKind.Simulated;

    /// <summary>优先级:数值越大越先调度(游戏核心文件 > 库 > 资源)</summary>
    public int Priority { get; init; } = TransferPriority.Normal;

    public IReadOnlyList<TransferSourceSpec> Sources { get; init; } = [];
    public string? ExpectedSha1 { get; init; }

    /// <summary>Http 任务的落盘完整路径(Simulated 任务忽略)。阶段二接入。</summary>
    public string? DestPath { get; init; }

    /// <summary>后台隐藏任务(资产补齐 / Java 获取等批量文件):不进任务中心列表,仅聚合成汇总。</summary>
    public bool Hidden { get; init; }

    /// <summary>总字节数;-1 表示未知(驱动不定长进度条)</summary>
    public long BytesTotal { get; init; } = -1;

    /// <summary>Simulated 任务的模拟速率(字节/秒)</summary>
    public double SimulatedSpeed { get; init; } = 1_500_000;
}

/// <summary>优先级约定常量(业务层可自定义数值)。</summary>
public static class TransferPriority
{
    public const int CoreGameFile = 50;
    public const int Library = 40;
    public const int AssetIndex = 30;
    public const int Asset = 20;
    public const int Normal = 10;
    public const int Low = 0;
}

/// <summary>
/// 引擎运行的任务单元:描述字段不可变,运行时字段只允许引擎(同程序集)写入;
/// UI 一律经由不可变的 TransferSnapshot 读取,避免跨线程竞态。
/// </summary>
public sealed class TransferTask
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public long Sequence { get; }
    public TransferSpec Spec { get; }

    internal TransferTask(TransferSpec spec, long sequence)
    {
        Spec = spec;
        Sequence = sequence;
    }

    // —— 引擎运行时状态(internal set,同程序集可写)——
    public TransferState State { get; internal set; } = TransferState.Queued;
    public long BytesReceived { get; internal set; }
    public double SpeedBytesPerSecond { get; internal set; }
    public string? Error { get; internal set; }
    public int Attempt { get; internal set; } = 1;
    public string CurrentSource { get; internal set; } = "";

    /// <summary>Pause(id) 置位;worker 被中断后据此区分「暂停」与「取消」并复位</summary>
    internal bool PauseRequested { get; set; }

    public bool IsActive => State is TransferState.Queued or TransferState.Downloading or TransferState.Verifying;

    /// <summary>终态:可从任务面板清除</summary>
    public bool IsFinished => State is TransferState.Completed or TransferState.Failed or TransferState.Cancelled;
}

/// <summary>发给 UI 的只读快照。</summary>
public sealed record TransferSnapshot(
    string Id,
    string DisplayName,
    TransferState State,
    long BytesTotal,
    long BytesReceived,
    double SpeedBytesPerSecond,
    int Attempt,
    int SourceCount,
    string CurrentSource,
    string? Error,
    int Priority,
    bool Hidden = false)
{
    /// <summary>是否应显示为「活动中」(用于排序)</summary>
    public bool ActiveLike => State is TransferState.Queued or TransferState.Downloading or TransferState.Verifying;
}

/// <summary>引擎参数(可运行时调整)。</summary>
public sealed class TransferOptions
{
    private int _maxConcurrency = 6;

    /// <summary>并发上限,默认 6,范围 2–16</summary>
    public int MaxConcurrency
    {
        get => _maxConcurrency;
        set => _maxConcurrency = Math.Clamp(value, 2, 16);
    }

    /// <summary>单个来源的最大尝试次数,超过后切换下一来源</summary>
    public int MaxAttemptsPerSource { get; set; } = 3;

    /// <summary>重试基础退避(毫秒),按次数指数增长</summary>
    public int RetryBaseDelayMs { get; set; } = 500;
}
