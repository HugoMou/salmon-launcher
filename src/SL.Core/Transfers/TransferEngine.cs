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
using System.Security.Cryptography;

namespace SL.Core.Transfers;

/// <summary>
/// Download engine: "task abstraction + bounded concurrent scheduling + multi-source fallback + retry backoff + snapshot events".
///
/// - Bounded concurrency: Pump() starts tasks from the queue by priority (high→low) within the concurrency limit;
/// - Multi-source fallback: the primary source retries MaxAttemptsPerSource times with exponential backoff, then switches to a mirror;
///   mirror order adapts to historical success rates (+1 on success / -1 on failure);
/// - Pause/cancel: the CTS flows into transport; pause keeps progress, resume/retry continues from the resume point;
/// - Events are snapshots: the engine only emits immutable TransferSnapshot, so the UI thread does zero file IO.
/// </summary>
public sealed class TransferEngine : ITransferService
{
    private readonly object _gate = new();
    private readonly List<TransferTask> _tasks = [];
    private readonly Dictionary<string, CancellationTokenSource> _cts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int Ok, int Fail)> _sourceStats = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Bytes, long AtMs)> _progressLast = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _lastRaiseMs = new(StringComparer.Ordinal);
    private readonly Dictionary<TransferKind, ITransport> _transports;
    private readonly TransferOptions _options;
    private int _inFlight;
    private long _sequence;

    public event Action<TransferSnapshot>? Updated;
    public event Action? StructureChanged;

    public TransferEngine(TransferOptions options)
    {
        _options = options;
        _transports = new Dictionary<TransferKind, ITransport>
        {
            [TransferKind.Simulated] = new SimulatedTransport(),
            [TransferKind.Http] = new HttpTransport(),
        };
    }

    public TransferOptions Options => _options;

    public bool HasActive
    {
        get
        {
            lock (_gate)
                return _tasks.Any(t => t.IsActive);
        }
    }

    public IReadOnlyList<TransferSnapshot> Snapshots
    {
        get
        {
            lock (_gate)
                return _tasks.Select(SnapOf).ToArray();
        }
    }

    // ============================== Submit ==============================

    public TransferTask Submit(TransferSpec spec)
    {
        var task = new TransferTask(spec, Interlocked.Increment(ref _sequence) - 1);

        lock (_gate)
        {
            task.State = TransferState.Queued;
            _tasks.Add(task);
        }

        RaiseTask(task, force: true);
        RaiseStructure();
        Pump();
        return task;
    }

    // ============================== Control: pause/resume/cancel/retry ==============================

    public void Pause(string id)
    {
        TransferTask? task;
        lock (_gate)
        {
            task = Find(id);
            if (task is null)
                return;

            switch (task.State)
            {
                case TransferState.Queued:
                    task.State = TransferState.Paused;
                    task.SpeedBytesPerSecond = 0;
                    break;

                case TransferState.Downloading or TransferState.Verifying:
                    // mark paused, then cancel the current attempt; the worker cleanup (FinishInterruption) sets Paused and keeps progress
                    task.PauseRequested = true;
                    if (_cts.TryGetValue(task.Id, out var workerCts))
                        workerCts.Cancel();
                    return; // the event is emitted by the worker cleanup

                default:
                    return; // a terminal task cannot be paused
            }
        }

        RaiseTask(task, force: true);
        RaiseStructure();
    }

    public void Resume(string id)
    {
        TransferTask? task;
        lock (_gate)
        {
            task = Find(id);
            if (task is null || task.State != TransferState.Paused)
                return;

            task.State = TransferState.Queued;
            task.Error = null;
        }

        RaiseTask(task, force: true);
        RaiseStructure();
        Pump();
    }

    public void Cancel(string id)
    {
        TransferTask? task;
        lock (_gate)
        {
            task = Find(id);
            if (task is null || task.IsFinished)
                return;

            switch (task.State)
            {
                case TransferState.Queued:
                    task.State = TransferState.Cancelled;
                    task.SpeedBytesPerSecond = 0;
                    break;

                case TransferState.Downloading or TransferState.Verifying:
                    // explicitly a cancel: clear the pause marker before cancelling the attempt
                    task.PauseRequested = false;
                    if (_cts.TryGetValue(task.Id, out var workerCts))
                        workerCts.Cancel();
                    return; // the event is emitted by the worker cleanup

                default:
                    return;
            }
        }

        RaiseTask(task, force: true);
        RaiseStructure();
    }

    public void Retry(string id)
    {
        TransferTask? task;
        lock (_gate)
        {
            task = Find(id);
            if (task is null || task.State is not (TransferState.Failed or TransferState.Cancelled))
                return;

            task.State = TransferState.Queued;
            task.BytesReceived = 0;
            task.Error = null;
            task.SpeedBytesPerSecond = 0;
            task.PauseRequested = false;
        }

        RaiseTask(task, force: true);
        RaiseStructure();
        Pump();
    }

    public void PauseAll()
    {
        var ids = ActiveIds();
        foreach (var id in ids)
            Pause(id);
    }

    public void ResumeAll()
    {
        var ids = PausedIds();
        foreach (var id in ids)
            Resume(id);
    }

    public void ClearFinished()
    {
        lock (_gate)
        {
            for (var i = _tasks.Count - 1; i >= 0; i--)
            {
                if (!_tasks[i].IsFinished)
                    continue;

                if (_cts.Remove(_tasks[i].Id, out var cts))
                    cts.Dispose();
                _tasks.RemoveAt(i);
            }
        }

        RaiseStructure();
    }

    // ============================== Scheduling ==============================

    /// <summary>Scheduling heartbeat: safe to call from any thread</summary>
    private void Pump()
    {
        var starts = new List<TransferTask>();

        lock (_gate)
        {
            while (_inFlight < _options.MaxConcurrency)
            {
                var next = _tasks
                    .Where(t => t.State == TransferState.Queued)
                    .OrderByDescending(t => t.Spec.Priority)
                    .ThenBy(t => t.Sequence)
                    .FirstOrDefault();
                if (next is null)
                    break;

                Begin(next);
                starts.Add(next);
            }
        }

        foreach (var task in starts)
            RunWorker(task);
    }

    /// <summary>A task has taken a concurrency slot and a CTS (called while the lock is held)</summary>
    private void Begin(TransferTask task)
    {
        task.State = TransferState.Downloading;
        task.Error = null;
        task.PauseRequested = false;
        task.CurrentSource = "";
        _inFlight++;
        _cts[task.Id] = new CancellationTokenSource();
    }

    private TransferTask? Find(string id) => _tasks.FirstOrDefault(t => t.Id == id);

    private List<string> ActiveIds()
    {
        lock (_gate)
            return _tasks.Where(t => t.IsActive).Select(t => t.Id).ToList();
    }

    private List<string> PausedIds()
    {
        lock (_gate)
            return _tasks.Where(t => t.State == TransferState.Paused).Select(t => t.Id).ToList();
    }

    // ============================== Worker lifetime ==============================

    /// <summary>async void is only started internally by the engine; callers guarantee a try/catch backstop.</summary>
    private async void RunWorker(TransferTask task)
    {
        try
        {
            await RunAttemptLoopAsync(task).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            FinishInterruption(task); // safety net: a cancellation missed on a rare path
        }
        catch (Exception ex)
        {
            Logging.AppLog.Error("Transfer", ex);
            MarkFailed(task, $"引擎内部错误:{ex.Message}");
        }
        finally
        {
            lock (_gate)
                _inFlight--;
            Pump();
        }
    }

    private async Task RunAttemptLoopAsync(TransferTask task)
    {
        var sources = OrderedSources(task.Spec.Sources);

        foreach (var source in sources)
        {
            lock (_gate)
            {
                task.BytesReceived = 0; // restart from zero after switching sources
                task.CurrentSource = source.Url;
            }

            for (var attempt = 1; attempt <= _options.MaxAttemptsPerSource; attempt++)
            {
                var cts = TryGetCts(task.Id);
                if (cts is null)
                    return; // the task was already cancelled and has been finalized

                long resume;
                lock (_gate)
                {
                    resume = attempt == 1 ? 0 : task.BytesReceived; // same-source retry keeps the resume point
                    task.Attempt = attempt;
                    task.State = TransferState.Downloading;
                    task.Error = null;
                }
                RaiseTask(task, force: true);

                TransportResult result;
                try
                {
                    var context = new TransferContext
                    {
                        Task = task,
                        SourceUrl = source.Url,
                        ResumeBytes = resume,
                        AttemptNumber = attempt,
                        Progress = offset => OnProgress(task, offset),
                    };
                    result = await TransportFor(task.Spec.Kind)
                        .TransferAsync(context, cts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    FinishInterruption(task);
                    return;
                }
                catch (Exception ex)
                {
                    Logging.AppLog.Warn("Transfer", $"{task.Spec.DisplayName} @ {source.Url} 抛出 {ex.GetType().Name}: {ex.Message}");
                    result = TransportResult.Fail(ex.Message);
                }

                if (result.Success)
                {
                    // stage 2: for Http tasks with an expected SHA-1, enter the verification stage before marking completion
                    var verifyError = await VerifySha1Async(task, cts.Token).ConfigureAwait(false);
                    if (verifyError is null)
                    {
                        Complete(task, source.Url);
                        return;
                    }

                    // verification failed → delete the corrupt prefix and continue as a failure of this attempt (same-source retry → mirror)
                    TryDeleteDest(task.Spec.DestPath);
                    lock (_gate)
                        task.BytesReceived = 0;
                    result = TransportResult.Fail(verifyError);
                }

                lock (_gate)
                {
                    NoteSource(source.Url, ok: false);
                    task.Error = result.FailureReason ?? "未知来源错误";
                }
                Logging.AppLog.Warn("Transfer", $"{task.Spec.DisplayName} 失败({source.Url}): {task.Error}");

                // same-source exponential backoff retry
                if (attempt < _options.MaxAttemptsPerSource)
                {
                    var backoff = _options.RetryBaseDelayMs * (1 << (attempt - 1));
                    var retryCts = TryGetCts(task.Id);
                    if (retryCts is null)
                        return;
                    try
                    {
                        await Task.Delay(backoff, retryCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        FinishInterruption(task);
                        return;
                    }
                }
            }

            // this source is out of attempts → next mirror (the top of the foreach resets BytesReceived)
        }

        var mirrorCount = Math.Max(0, sources.Count - 1);
        MarkFailed(task, mirrorCount > 0
            ? $"主源与 {mirrorCount} 个镜像源均失败。请检查网络后重试。"
            : "下载源失败。请检查网络后重试。");
    }

    // ============================== Final SHA-1 check (stage 2) ==============================

    /// <summary>Http tasks with ExpectedSha1: compute the file SHA-1 on the fly and compare. null = no check needed or the check passed.</summary>
    private async Task<string?> VerifySha1Async(TransferTask task, CancellationToken token)
    {
        var expected = task.Spec.ExpectedSha1;
        var dest = task.Spec.DestPath;
        if (string.IsNullOrWhiteSpace(expected)
            || task.Spec.Kind != TransferKind.Http
            || string.IsNullOrEmpty(dest))
            return null;

        lock (_gate)
        {
            task.State = TransferState.Verifying;
            task.Error = null;
        }
        RaiseTask(task, force: true);
        RaiseStructure();

        try
        {
            if (!File.Exists(dest))
                return "文件未落盘(下载结果缺失)";

            await using var fs = new FileStream(
                dest, FileMode.Open, FileAccess.Read, FileShare.Read,
                1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var sha = SHA1.Create();
            var hash = await sha.ComputeHashAsync(fs, token).ConfigureAwait(false);
            var actual = Convert.ToHexString(hash).ToLowerInvariant();

            return actual.Equals(expected.Trim(), StringComparison.OrdinalIgnoreCase)
                ? null
                : $"SHA-1 不匹配(期望 {ShortSha(expected)},实际 {ShortSha(actual)})";
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logging.AppLog.Error("Transfer.Sha1", ex);
            return $"校验失败:{ex.Message}";
        }
    }

    private static string ShortSha(string sha)
        => sha.Length <= 12 ? sha : sha[..8] + "…";

    /// <summary>Delete the corrupt on-disk file (best effort; a failure does not block the retry).</summary>
    private static void TryDeleteDest(string? dest)
    {
        if (string.IsNullOrEmpty(dest) || !File.Exists(dest))
            return;
        try
        {
            File.Delete(dest);
        }
        catch (IOException)
        {
            // file in use, etc.: ignore; the next PrepareLocalPrefix resumes from the prefix and verification covers the rest
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void Complete(TransferTask task, string sourceUrl)
    {        lock (_gate)
        {
            NoteSource(sourceUrl, ok: true);
            task.State = TransferState.Completed;
            task.Error = null;
            task.SpeedBytesPerSecond = 0;
            DisposeCts(task);
        }
        RaiseTask(task, force: true);
        RaiseStructure();
    }

    private void MarkFailed(TransferTask task, string reason)
    {
        lock (_gate)
        {
            task.State = TransferState.Failed;
            task.Error = reason;
            task.SpeedBytesPerSecond = 0;
            DisposeCts(task);
        }
        RaiseTask(task, force: true);
        RaiseStructure();
    }

    /// <summary>Cleanup after a worker is cancelled: tell pause and cancel apart via PauseRequested.</summary>
    private void FinishInterruption(TransferTask task)
    {
        lock (_gate)
        {
            task.SpeedBytesPerSecond = 0;
            var wasPause = task.PauseRequested;
            task.PauseRequested = false;

            if (wasPause)
            {
                task.State = TransferState.Paused; // keep BytesReceived so the resume continues
            }
            else
            {
                task.State = TransferState.Cancelled;
                task.Error = null;
            }

            DisposeCts(task);
        }
        RaiseTask(task, force: true);
        RaiseStructure();
    }

    // ============================== Progress and events ==============================

    private void OnProgress(TransferTask task, long absolute)
    {
        lock (_gate)
        {
            var now = Environment.TickCount64;

            if (_progressLast.TryGetValue(task.Id, out var last))
            {
                if (absolute < last.Bytes)
                {
                    // progress went backwards (source switch): reset the sampling baseline
                    _progressLast[task.Id] = (absolute, now);
                }
                else
                {
                    var dt = now - last.AtMs;
                    if (dt >= 120)
                    {
                        var rate = (absolute - last.Bytes) * 1000.0 / Math.Max(1, dt);
                        task.SpeedBytesPerSecond = task.SpeedBytesPerSecond <= 0
                            ? rate
                            : task.SpeedBytesPerSecond * 0.65 + rate * 0.35; // EMA smoothing
                        _progressLast[task.Id] = (absolute, now);
                    }
                }
            }
            else
            {
                _progressLast[task.Id] = (absolute, now);
            }

            task.BytesReceived = absolute;
        }

        RaiseTask(task, force: false);
    }

    private void RaiseTask(TransferTask task, bool force)
    {
        if (!force)
        {
            lock (_gate)
            {
                var now = Environment.TickCount64;
                if (_lastRaiseMs.TryGetValue(task.Id, out var last) && now - last < 90)
                    return;
                _lastRaiseMs[task.Id] = now;
            }
        }

        var snapshot = SnapOf(task);
        try
        {
            Updated?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            Logging.AppLog.Error("Transfer.Updated", ex);
        }
    }

    private void RaiseStructure()
    {
        try
        {
            StructureChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Logging.AppLog.Error("Transfer.StructureChanged", ex);
        }
    }

    private static TransferSnapshot SnapOf(TransferTask task)
        => new(task.Id, task.Spec.DisplayName, task.State, task.Spec.BytesTotal, task.BytesReceived,
               task.SpeedBytesPerSecond, task.Attempt, task.Spec.Sources.Count, task.CurrentSource,
               task.Error, task.Spec.Priority, task.Spec.Hidden);

    // ============================== Multi-source and transport ==============================

    private void NoteSource(string url, bool ok)
    {
        var (okCount, failCount) = _sourceStats.TryGetValue(url, out var v) ? v : (0, 0);
        _sourceStats[url] = ok ? (okCount + 1, failCount) : (okCount, failCount + 1);
    }

    private List<TransferSourceSpec> OrderedSources(IReadOnlyList<TransferSourceSpec> specs)
    {
        if (specs.Count <= 1)
            return specs.ToList();

        var primary = new List<TransferSourceSpec>();
        var mirrors = new List<TransferSourceSpec>();
        foreach (var s in specs)
            (s.IsMirror ? mirrors : primary).Add(s);

        mirrors.Sort((a, b) =>
        {
            var aScore = _sourceStats.TryGetValue(a.Url, out var av) ? av.Ok - av.Fail : 0;
            var bScore = _sourceStats.TryGetValue(b.Url, out var bv) ? bv.Ok - bv.Fail : 0;
            return bScore.CompareTo(aScore);
        });

        return primary.Concat(mirrors).ToList();
    }

    private ITransport TransportFor(TransferKind kind)
    {
        if (_transports.TryGetValue(kind, out var transport))
            return transport;

        throw new NotSupportedException($"传输类型 {kind} 的实现尚未接入(计划于阶段二)");
    }

    private CancellationTokenSource? TryGetCts(string id)
    {
        lock (_gate)
            return _cts.TryGetValue(id, out var cts) ? cts : null;
    }

    private void DisposeCts(TransferTask task)
    {
        if (_cts.Remove(task.Id, out var cts))
            cts.Dispose();
    }
}
