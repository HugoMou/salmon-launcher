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
using Microsoft.UI.Xaml.Controls;

namespace SL.App.Notify;

public enum NotifyKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>A single toast (currently on screen), mapped to an InfoBar.</summary>
public sealed class Toast : Infrastructure.VmBase
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public NotifyKind Kind { get; }
    public string Text { get; }
    internal string MergeKey { get; }

    public InfoBarSeverity Severity => Kind switch
    {
        NotifyKind.Success => InfoBarSeverity.Success,
        NotifyKind.Warning => InfoBarSeverity.Warning,
        NotifyKind.Error => InfoBarSeverity.Error,
        _ => InfoBarSeverity.Informational,
    };

    private int _count = 1;
    public int Count
    {
        get => _count;
        set
        {
            if (Set(ref _count, value))
                On(nameof(CountText));
        }
    }

    public string CountText => Count > 1 ? $" ×{Count}" : "";

    private bool _isOpen = true;
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (Set(ref _isOpen, value) && !value)
                Closed?.Invoke();
        }
    }

    /// <summary>Raised when the user clicks the InfoBar close button; the service dequeues it and shows the next one.</summary>
    public event Action? Closed;

    internal Toast(NotifyKind kind, string text)
    {
        Kind = kind;
        Text = text;
        MergeKey = $"{kind}|{text}";
    }
}

/// <summary>
/// Toast layer service: non-modal InfoBar notifications.
/// - Shows the queue one at a time (at most MaxVisible on screen);
/// - Repeated notifications of the same kind (same Kind + text) merge into one with an incremented count;
/// - Thread-safe: a Publish call from a non-UI thread is forwarded to the UI thread automatically.
/// </summary>
public sealed class NotifyService
{
    private readonly DispatcherQueue _ui;
    private readonly Queue<Toast> _pending = [];
    private readonly Dictionary<string, Toast> _visibleByKey = [];
    private readonly Dictionary<string, DispatcherQueueTimer> _timers = [];

    /// <summary>Maximum number of toasts visible at once (shown one by one, default 1; raise it to allow stacking)</summary>
    public int MaxVisible { get; set; } = 1;

    /// <summary>How long a single toast stays on screen</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMilliseconds(4200);

    public ObservableCollection<Toast> Items { get; } = [];

    public NotifyService()
    {
        _ui = DispatcherQueue.GetForCurrentThread();
    }

    public void Publish(NotifyKind kind, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        _ui.TryEnqueue(() => PublishCore(kind, text));
    }

    private void PublishCore(NotifyKind kind, string text)
    {
        var key = $"{kind}|{text}";

        // 1) Already visible: merge (count +1 and restart the lifetime)
        if (_visibleByKey.TryGetValue(key, out var shown))
        {
            shown.Count++;
            RestartTimer(shown);
            return;
        }

        // 2) Waiting in the queue: merge
        if (_pending.FirstOrDefault(t => t.MergeKey == key) is Toast queued)
        {
            queued.Count++;
            return;
        }

        // 3) New: show immediately if a visible slot is free, otherwise enqueue
        var toast = new Toast(kind, text);
        if (Items.Count < MaxVisible && _pending.Count == 0)
            Show(toast);
        else
            _pending.Enqueue(toast);
    }

    private void Show(Toast toast)
    {
        _visibleByKey[toast.MergeKey] = toast;
        toast.Closed += () => Dismiss(toast);
        Items.Add(toast);

        var timer = _ui.CreateTimer();
        timer.Interval = Lifetime;
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Dismiss(toast);
        _timers[toast.Id] = timer;
        timer.Start();
    }

    private void Dismiss(Toast toast)
    {
        if (_timers.Remove(toast.Id, out var timer))
            timer.Stop();

        Items.Remove(toast);
        _visibleByKey.Remove(toast.MergeKey);
        ShowNext();
    }

    private void ShowNext()
    {
        if (Items.Count >= MaxVisible || _pending.Count == 0)
            return;
        Show(_pending.Dequeue());
    }

    private void RestartTimer(Toast toast)
    {
        if (_timers.TryGetValue(toast.Id, out var timer))
        {
            timer.Stop();
            timer.Start();
        }
    }
}
