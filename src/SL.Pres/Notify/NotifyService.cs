using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using SL.Pres.Infrastructure;

namespace SL.Pres.Notify;

public enum NotifyKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>单条提示(显示中)。</summary>
public sealed class Toast : VmBase
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public NotifyKind Kind { get; }
    public string Text { get; }
    internal string MergeKey { get; }

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

    public string CountText => Count > 1 ? $" ×{_count}" : "";

    public string Glyph => Kind switch
    {
        NotifyKind.Success => "✔",
        NotifyKind.Warning => "⚠",
        NotifyKind.Error => "✖",
        _ => "ℹ",
    };

    /// <summary>XAML 触发器用的字符串键(DataTrigger 与枚举比较需字符串)</summary>
    public string KindKey => Kind.ToString();

    internal Toast(NotifyKind kind, string text)
    {
        Kind = kind;
        Text = text;
        MergeKey = $"{kind}|{text}";
    }
}

/// <summary>
/// 提示层服务:非模态临时通知。
/// - 队列逐条显示(同屏 ≤ MaxVisible);
/// - 同类型(Kind+文本相同)重复提示自动合并为一条并累加计数;
/// - 线程安全:非 UI 线程调用 Publish 会自动转发到 UI 线程。
/// </summary>
public sealed class NotifyService : VmBase
{
    private readonly Dispatcher _ui;
    private readonly Queue<Toast> _pending = [];
    private readonly Dictionary<string, Toast> _visibleByKey = [];
    private readonly Dictionary<string, DispatcherTimer> _timers = [];

    /// <summary>同时可见的最大条数(逐条显示,默认 1,可调大允许堆叠)</summary>
    public int MaxVisible { get; set; } = 1;

    /// <summary>单条提示停留时长</summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMilliseconds(4200);

    public ObservableCollection<Toast> Items { get; } = [];

    public NotifyService()
    {
        _ui = Dispatcher.CurrentDispatcher;
    }

    public void Publish(NotifyKind kind, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        _ui.BeginInvoke(() => PublishCore(kind, text));
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
        Items.Add(toast);

        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = Lifetime,
        };
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
