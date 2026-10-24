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

/// <summary>单条提示(显示中),映射到 InfoBar 展示。</summary>
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

    /// <summary>用户点击 InfoBar 关闭按钮时触发,服务据此出队并显示下一条。</summary>
    public event Action? Closed;

    internal Toast(NotifyKind kind, string text)
    {
        Kind = kind;
        Text = text;
        MergeKey = $"{kind}|{text}";
    }
}

/// <summary>
/// 提示层服务:非模态 InfoBar 通知。
/// - 队列逐条显示(同屏 ≤ MaxVisible);
/// - 同类型(Kind+文本相同)重复提示自动合并为一条并累加计数;
/// - 线程安全:非 UI 线程调用 Publish 会自动转发到 UI 线程。
/// </summary>
public sealed class NotifyService
{
    private readonly DispatcherQueue _ui;
    private readonly Queue<Toast> _pending = [];
    private readonly Dictionary<string, Toast> _visibleByKey = [];
    private readonly Dictionary<string, DispatcherQueueTimer> _timers = [];

    /// <summary>同时可见的最大条数(逐条显示,默认 1,可调大允许堆叠)</summary>
    public int MaxVisible { get; set; } = 1;

    /// <summary>单条提示停留时长</summary>
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

        // 1) 已在显示:合并(计数 +1 并重置存活时间)
        if (_visibleByKey.TryGetValue(key, out var shown))
        {
            shown.Count++;
            RestartTimer(shown);
            return;
        }

        // 2) 在队列中等待:合并
        if (_pending.FirstOrDefault(t => t.MergeKey == key) is Toast queued)
        {
            queued.Count++;
            return;
        }

        // 3) 新建:可见槽位够则立即显示,否则排队
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
