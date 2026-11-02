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
using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using SL.Core.Transfers;
using SL.App.Infrastructure;
using SL.App.Localization;

namespace SL.App.Tasks;

/// <summary>Task-row view model: engine snapshot → bindable UI projection (fields match the old SL.Pres version).</summary>
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

    /// <summary>Run badge next to the title: "Done" when completed, empty otherwise</summary>
    public string RunBadge => _snap.State == TransferState.Completed ? Loc.T("完成") : "";

    public string StateText => _snap.State switch
    {
        TransferState.Queued => Loc.T("排队中"),
        TransferState.Downloading => Loc.T("下载中"),
        TransferState.Verifying => Loc.T("校验中"),
        TransferState.Paused => Loc.T("已暂停"),
        TransferState.Completed => Loc.T("已完成"),
        TransferState.Failed => Loc.T("失败"),
        TransferState.Cancelled => Loc.T("已取消"),
        _ => "",
    };

    /// <summary>Total size unknown (-1) while active: the progress bar runs indeterminate</summary>
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
            return seconds >= 90 ? string.Format(Loc.T("剩 {0} 分钟"), seconds / 60) : string.Format(Loc.T("剩 {0} 秒"), seconds);
        }
    }

    public string? ErrorText => _snap.Error;
}

/// <summary>Task center view model: aggregates the engine task list and exposes bulk-operation commands.</summary>
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
                || r.StateText == Loc.T("排队中") || r.StateText == Loc.T("校验中"));
            return active > 0 ? string.Format(Loc.T("{0} 个任务进行中 · 共 {1} 项"), active, Rows.Count) : string.Format(Loc.T("共 {0} 项"), Rows.Count);
        }
    }

    /// <summary>Rebuild snapshots when entering the page (the engine may have submitted tasks elsewhere).</summary>
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

    /// <summary>Show the empty-state card when the list is empty.</summary>
    public Microsoft.UI.Xaml.Visibility EmptyVisibility
        => Rows.Count == 0 ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
}
