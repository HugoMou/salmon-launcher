using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using SL.Core.Transfers;
using SL.App.Infrastructure;

namespace SL.App.Tasks;

/// <summary>任务卡行视图模型:引擎快照 → UI 可绑定投影(与旧版 SL.Pres 字段一致)。</summary>
public sealed class TaskRowVM : VmBase
{
    public string Id { get; }
    private TransferSnapshot _snap;

    public TaskRowVM(TransferSnapshot snapshot)
    {
        Id = snapshot.Id;
        _snap = snapshot;

        PauseCommand = new RelayCommand(
            () => AppServices.Transfers.Pause(Id),
            () => _snap.State is TransferState.Queued or TransferState.Downloading or TransferState.Verifying);
        ResumeCommand = new RelayCommand(
            () => AppServices.Transfers.Resume(Id),
            () => _snap.State == TransferState.Paused);
        CancelCommand = new RelayCommand(
            () => AppServices.Transfers.Cancel(Id),
            () => _snap.State is TransferState.Queued or TransferState.Downloading or TransferState.Verifying or TransferState.Paused);
        RetryCommand = new RelayCommand(
            () => AppServices.Transfers.Retry(Id),
            () => _snap.State is TransferState.Failed or TransferState.Cancelled);
    }

    public RelayCommand PauseCommand { get; }
    public RelayCommand ResumeCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand RetryCommand { get; }

    public void Apply(TransferSnapshot snapshot)
    {
        _snap = snapshot;
        On(nameof(StateText));
        On(nameof(IsIndeterminate));
        On(nameof(Progress));
        On(nameof(ProgressText));
        On(nameof(SpeedText));
        On(nameof(RemainText));
        On(nameof(RunBadge));
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
    }

    public string Title => _snap.DisplayName;

    /// <summary>标题旁的运行标识:完成时显示"完成",其余为空</summary>
    public string RunBadge => _snap.State == TransferState.Completed ? "完成" : "";

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

    /// <summary>总大小未知(-1)且活动中:进度条走不确定态</summary>
    public bool IsIndeterminate => _snap.BytesTotal < 0 && _snap.ActiveLike;

    public double Progress
    {
        get
        {
            var total = _snap.BytesTotal;
            return total > 0 ? Math.Clamp(_snap.BytesReceived / (double)total, 0, 1) : 0;
        }
    }

    public string ProgressText
    {
        get
        {
            if (_snap.BytesTotal < 0)
                return BytesText.FormatBytes(_snap.BytesReceived);
            return $"{BytesText.FormatBytes(_snap.BytesReceived)} / {BytesText.FormatBytes(_snap.BytesTotal)}";
        }
    }

    public string SpeedText => _snap.State == TransferState.Downloading
        ? BytesText.FormatSpeed(_snap.SpeedBytesPerSecond)
        : "";

    public string RemainText
    {
        get
        {
            if (_snap.State != TransferState.Downloading || _snap.BytesTotal <= 0 || _snap.SpeedBytesPerSecond <= 0)
                return "";
            var remainBytes = _snap.BytesTotal - _snap.BytesReceived;
            if (remainBytes <= 0)
                return "";
            var seconds = (int)(remainBytes / _snap.SpeedBytesPerSecond);
            return seconds >= 90 ? $"剩 {seconds / 60} 分钟" : $"剩 {seconds} 秒";
        }
    }

    public string? ErrorText => _snap.Error;
}

/// <summary>任务中心视图模型:聚合引擎任务列表,提供批量操作命令。</summary>
public sealed class TaskCenterVM : VmBase, IRefreshable
{
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();

    public ObservableCollection<TaskRowVM> Rows { get; } = [];

    public RelayCommand ClearFinishedCommand { get; }
    public RelayCommand PauseAllCommand { get; }
    public RelayCommand ResumeAllCommand { get; }

    public TaskCenterVM()
    {
        ClearFinishedCommand = new RelayCommand(() => AppServices.Transfers.ClearFinished());
        PauseAllCommand = new RelayCommand(() => AppServices.Transfers.PauseAll());
        ResumeAllCommand = new RelayCommand(() => AppServices.Transfers.ResumeAll());

        AppServices.Transfers.Updated += OnEngineUpdated;
        AppServices.Transfers.StructureChanged += OnStructureChanged;
        Rebuild();
    }

    public string Summary
    {
        get
        {
            var active = Rows.Count(r => r.IsIndeterminate
                || !string.IsNullOrEmpty(r.SpeedText)
                || r.StateText is "排队中" or "校验中");
            return active > 0 ? $"{active} 个任务进行中 · 共 {Rows.Count} 项" : $"共 {Rows.Count} 项";
        }
    }

    /// <summary>进入页面时重建快照(引擎可能在别处提交了任务)。</summary>
    public void RefreshData() => _ui.TryEnqueue(Rebuild);

    private void OnEngineUpdated(TransferSnapshot snap)
    {
        if (snap.Hidden)
            return;
        _ui.TryEnqueue(() =>
        {
            if (Rows.FirstOrDefault(r => r.Id == snap.Id) is not { } row)
                return;
            row.Apply(snap);
            On(nameof(Summary));
        });
    }

    private void OnStructureChanged() => _ui.TryEnqueue(Rebuild);

    private void Rebuild()
    {
        Rows.Clear();
        foreach (var snap in AppServices.Transfers.Snapshots.Where(s => !s.Hidden))
            Rows.Add(new TaskRowVM(snap));
        On(nameof(Summary));
        On(nameof(EmptyVisibility));
    }

    /// <summary>列表为空时显示空状态卡。</summary>
    public Microsoft.UI.Xaml.Visibility EmptyVisibility
        => Rows.Count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
}
