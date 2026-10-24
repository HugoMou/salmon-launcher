using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using SL.Core.Transfers;
using SL.Pres.Infrastructure;

namespace SL.Pres.Tasks;

/// <summary>任务卡行视图模型:引擎快照 → UI 可绑定投影。</summary>
public sealed class TaskRowVM : VmBase
{
    public string Id { get; }
    private TransferSnapshot _snap;

    public TaskRowVM(TransferSnapshot snapshot)
    {
        Id = snapshot.Id;
        _snap = snapshot;

        PauseCommand = new RelayCommand(() => AppServices.Transfers.Pause(Id),
            () => _snap.State is TransferState.Queued or TransferState.Downloading or TransferState.Verifying);
        ResumeCommand = new RelayCommand(() => AppServices.Transfers.Resume(Id),
            () => _snap.State == TransferState.Paused);
        CancelCommand = new RelayCommand(() => AppServices.Transfers.Cancel(Id),
            () => _snap.State is TransferState.Queued or TransferState.Downloading or TransferState.Verifying or TransferState.Paused);
        RetryCommand = new RelayCommand(() => AppServices.Transfers.Retry(Id),
            () => _snap.State is TransferState.Failed or TransferState.Cancelled);
    }

    public void Apply(TransferSnapshot snapshot)
    {
        _snap = snapshot;
        On(nameof(Title));
        On(nameof(RowState));
        On(nameof(StateText));
        On(nameof(Progress));
        On(nameof(BytesText));
        On(nameof(SpeedText));
        On(nameof(RemainText));
        On(nameof(ErrorText));
        On(nameof(RunTitle));
    }

    public string Title => _snap.DisplayName;
    public TransferState RowState => _snap.State;

    /// <summary>标题旁的运行标识:失败/完成时置空</summary>
    public string RunTitle => _snap.State is TransferState.Completed ? "完成" : "";

    public string StateText => _snap.State switch
    {
        TransferState.Queued => "排队中",
        TransferState.Downloading => "下载中",
        TransferState.Verifying => "校验中",
        TransferState.Paused => "已暂停",
        TransferState.Completed => "已完成",
        TransferState.Failed => "失败",
        TransferState.Cancelled => "已取消",
        _ => "",
    };

    public double Progress
    {
        get
        {
            var total = _snap.BytesTotal;
            return total > 0 ? Math.Clamp(_snap.BytesReceived / (double)total, 0, 1) : 0;
        }
    }

    public string BytesText
    {
        get
        {
            var received = Infrastructure.BytesText.FormatBytes(_snap.BytesReceived);
            var total = _snap.BytesTotal > 0
                ? Infrastructure.BytesText.FormatBytes(_snap.BytesTotal)
                : "未知";
            return $"{received} / {total}";
        }
    }

    public string SpeedText => _snap.State == TransferState.Downloading
        ? Infrastructure.BytesText.FormatSpeed(_snap.SpeedBytesPerSecond)
        : "";

    public string RemainText
    {
        get
        {
            if (_snap.State != TransferState.Downloading || _snap.BytesTotal <= 0 || _snap.SpeedBytesPerSecond <= 0)
                return "";
            var remain = (_snap.BytesTotal - _snap.BytesReceived) / _snap.SpeedBytesPerSecond;
            return remain >= 3600
                ? $"剩余约 {remain / 3600:0.0} 小时"
                : remain >= 90
                    ? $"剩余约 {remain / 60:0} 分钟"
                    : $"剩余约 {remain:0} 秒";
        }
    }

    public string ErrorText => _snap.Error ?? "";

    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RetryCommand { get; }
}

/// <summary>
/// 任务中心:唯一绑定引擎事件的任务列表视图模型(subshell 中所有任务页共享)。
/// - 行更新走 Updated(快照);结构变化(新增/清理)走 StructureChanged 重建列表;
/// - 排序:活动中(下载/校验/排队)在前,其余按完成/失败靠后。
/// </summary>
public sealed class TaskCenterVM : VmBase
{
    public ObservableCollection<TaskRowVM> Rows { get; } = [];
    private readonly Dictionary<string, TaskRowVM> _byId = new(StringComparer.Ordinal);

    public TaskCenterVM()
    {
        AppServices.Transfers.Updated += OnSnapshot;
        AppServices.Transfers.StructureChanged += RebuildCore;

        RebuildCore();
    }

    public bool EmptyVisible => Rows.Count == 0;

    public string SummaryText
    {
        get
        {
            var snaps = AppServices.Transfers.Snapshots.Where(s => !s.Hidden).ToArray();
            var active = snaps.Count(s => s.ActiveLike);
            var done = snaps.Count(s => s.State == TransferState.Completed);
            var failed = snaps.Count(s => s.State == TransferState.Failed);
            var baseText = $"进行 {active} · 完成 {done} · 失败 {failed}";
            return _hiddenById.Count > 0 ? $"{baseText} · {HiddenText}" : baseText;
        }
    }

    /// <summary>汇总变化(启动面板等页订阅刷新)</summary>
    public event Action? SummaryChanged;

    public string ConcurrencyNote => $"并发上限 {AppServices.Transfers.Options.MaxConcurrency} 个任务";

    // ---------------- 事件(线程封送) ----------------

    private void OnSnapshot(TransferSnapshot snapshot)
    {
        Application.Current?.Dispatcher.BeginInvoke(() => SyncRow(snapshot));
    }

    private void RebuildCore()
    {
        Application.Current?.Dispatcher.BeginInvoke(Rebuild);
    }

    private void SyncRow(TransferSnapshot snapshot)
    {
        // 隐藏任务(资产补齐 / Java 获取):不建行,仅聚合汇总
        if (snapshot.Hidden)
        {
            _hiddenById[snapshot.Id] = snapshot;
            On(nameof(HiddenText));
            UpdateSummary();
            return;
        }

        if (_byId.TryGetValue(snapshot.Id, out var row))
        {
            row.Apply(snapshot);
            UpdateSummary();
            return;
        }

        var newRow = new TaskRowVM(snapshot);
        _byId[snapshot.Id] = newRow;
        Rows.Add(newRow);
        UpdateSummary();
    }

    private readonly Dictionary<string, TransferSnapshot> _hiddenById = new(StringComparer.Ordinal);

    /// <summary>隐藏任务聚合一览(如"资产补齐 1200 / 4411")。</summary>
    public string HiddenText
    {
        get
        {
            if (_hiddenById.Count == 0)
                return "";
            var done = _hiddenById.Values.Count(v => v.State == TransferState.Completed);
            var failed = _hiddenById.Values.Count(v => v.State is TransferState.Failed or TransferState.Cancelled);
            var active = _hiddenById.Values.Count(v => v.ActiveLike);
            return failed > 0
                ? $"后台文件 {done}/{_hiddenById.Count} 完成 · {failed} 失败 · {active} 进行中"
                : $"后台文件 {done}/{_hiddenById.Count} · {active} 进行中";
        }
    }

    private void Rebuild()
    {
        var snapshots = AppServices.Transfers.Snapshots;

        var alive = new HashSet<string>(StringComparer.Ordinal);
        var aliveHidden = new HashSet<string>(StringComparer.Ordinal);
        foreach (var snapshot in snapshots)
        {
            if (snapshot.Hidden)
            {
                _hiddenById[snapshot.Id] = snapshot;
                aliveHidden.Add(snapshot.Id);
                continue;
            }

            alive.Add(snapshot.Id);
            if (!_byId.TryGetValue(snapshot.Id, out var row))
            {
                row = new TaskRowVM(snapshot);
                _byId[snapshot.Id] = row;
            }
            row.Apply(snapshot);
        }

        foreach (var hideId in _hiddenById.Keys.Where(k => !aliveHidden.Contains(k)).ToList())
            _hiddenById.Remove(hideId);

        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (!alive.Contains(Rows[i].Id))
            {
                _byId.Remove(Rows[i].Id);
                Rows.RemoveAt(i);
            }
        }

        // 排序:活动中在前,失败其次,完成/取消靠后
        static int GroupOrder(TaskRowVM r) => r.RowState switch
        {
            TransferState.Downloading or TransferState.Verifying => 0,
            TransferState.Queued => 1,
            TransferState.Paused => 2,
            TransferState.Failed => 3,
            TransferState.Cancelled => 4,
            _ => 5, // Completed
        };

        var ordered = Rows.OrderBy(GroupOrder).ThenBy(r => r.Title, StringComparer.Ordinal).ToList();
        if (!ordered.SequenceEqual(Rows))
        {
            Rows.Clear();
            foreach (var row in ordered)
                Rows.Add(row);
        }

        UpdateSummary();
        On(nameof(HiddenText));
    }

    private void UpdateSummary()
    {
        On(nameof(SummaryText));
        On(nameof(ConcurrencyNote));
        On(nameof(EmptyVisible));
        try
        {
            SummaryChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Core.Logging.AppLog.Error("TaskCenter.SummaryChanged", ex);
        }
    }

    // ---------------- 命令 ----------------

    public ICommand PauseAllCommand { get; } = new RelayCommand(() => AppServices.Transfers.PauseAll());
    public ICommand ResumeAllCommand { get; } = new RelayCommand(() => AppServices.Transfers.ResumeAll());
    public ICommand ClearDoneCommand { get; } = new RelayCommand(() => AppServices.Transfers.ClearFinished());

    public ICommand SpawnDemoCommand { get; } = new RelayCommand(() =>
    {
        for (var i = 1; i <= 3; i++)
            DemoTasks.SpawnSample($"演示文件 {i}.part", TransferPriority.Normal);
    });
}
