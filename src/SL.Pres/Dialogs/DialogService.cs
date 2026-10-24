using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SL.Pres.Infrastructure;

namespace SL.Pres.Dialogs;

/// <summary>提示口味(信息/成功/警告/错误/询问)。</summary>
public enum MsgMark
{
    Info,
    Success,
    Warning,
    Error,
    Question,
}

/// <summary>登录框(阶段五前的界面脚手架)的演示结果。</summary>
public sealed record LoginDraftResult(string Name, bool OfflineMode);

/// <summary>对话框宿主带 INPC 的基类,由对话框层的舞台(host)统一装载。</summary>
public abstract class DialogPanel : UserControl, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));

    /// <summary>对话框结果就绪(用户点确定/取消/按 ESC/点击背板)。null 表示取消。</summary>
    public event Action<object?>? Finish;

    protected void Complete(object? result)
    {
        if (Finished)
            return;
        Finished = true;
        Finish?.Invoke(result);
    }

    private bool Finished { get; set; }

    /// <summary>背板点击 / 超时等场景下触发的「取消」语义</summary>
    public void RequestCancel()
    {
        OnCanceled();
    }

    protected abstract void OnCanceled();
}

/// <summary>
/// 模态对话框服务:对话框统一渲染在主窗口的对话框层(覆盖内容区),
/// 打开期间底层内容进入不可交互状态,由 MainWindow 订阅 StateChanged 实现。
/// 对话框按请求顺序排队依次展示。
/// </summary>
public sealed class DialogService
{
    private ContentPresenter? _stage;
    private readonly Queue<(FrameworkElement View, TaskCompletionSource<object?> Source)> _pending = [];
    private FrameworkElement? _activeView;

    /// <summary>打开/关闭状态变化(用于禁用下层内容)</summary>
    public event Action<bool>? StateChanged;

    public bool IsOpen => _activeView is not null;

    public void Attach(ContentPresenter stage)
    {
        _stage = stage;
        stage.Content = null;
        stage.Visibility = Visibility.Collapsed;
    }

    // ============================== 常用对话框 ==============================

    public Task<bool> ConfirmAsync(string title, string message, MsgMark mark = MsgMark.Question,
        string? okLabel = null, string? cancelLabel = null)
    {
        var view = new DialogMsgView();
        view.Configure(title, message, mark, askCancel: true, okLabel, cancelLabel);
        return AwaitBool(Run(view));
    }

    public Task<bool> AlertAsync(string title, string message, MsgMark mark = MsgMark.Info)
    {
        var view = new DialogMsgView();
        view.Configure(title, message, mark, askCancel: false, null, null);
        return AwaitBool(Run(view));
    }

    public async Task<string?> PromptAsync(string title, string? hint, string initial = "")
    {
        var view = new DialogInputView();
        view.Configure(title, hint ?? "", initial);
        var result = await Run(view).ConfigureAwait(false);
        return result as string;
    }

    public async Task<int?> ChooseAsync(string title, string? hint, IReadOnlyList<string> options)
    {
        var view = new DialogSelectView();
        view.Configure(title, hint ?? "", options);
        var result = await Run(view).ConfigureAwait(false);
        return result as int?;
    }

    public async Task<LoginDraftResult?> LoginAsync()
    {
        var view = new DialogLoginView();
        var result = await Run(view).ConfigureAwait(false);
        return result as LoginDraftResult;
    }

    // ============================== 舞台调度 ==============================

    private static async Task<bool> AwaitBool(Task<object?> task)
    {
        var result = await task.ConfigureAwait(false);
        return result is bool ok && ok;
    }

    /// <summary>把面板放入舞台;同一时刻只有一个,后续请求排队。</summary>
    private async Task<object?> Run(FrameworkElement view)
    {
        var stage = _stage ?? throw new InvalidOperationException("对话框层尚未初始化(Attach 未调用)");

        if (!stage.CheckAccess())
        {
            // 非常罕见的非 UI 线程调用:转发到 UI 线程
            var nested = await stage.Dispatcher.InvokeAsync(() => Run(view)).Task.ConfigureAwait(true);
            return await nested.ConfigureAwait(true);
        }

        var source = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        if (_activeView is null)
        {
            Present(stage, view, source);
        }
        else
        {
            _pending.Enqueue((view, source));
        }

        // 修复:async 方法里绝不能 return Task 本身;必须 await 解包,否则调用方拿到的是"Task 当结果"
        return await source.Task.ConfigureAwait(true);
    }

    private void Present(ContentPresenter stage, FrameworkElement view, TaskCompletionSource<object?> source)
    {
        _activeView = view;

        DialogPanel panel = (DialogPanel)view;

        void Handler(object? result)
        {
            panel.Finish -= Handler; // 一次性订阅
            CloseCore(stage, result, source);
        }
        panel.Finish += Handler;

        Core.Logging.AppLog.Info("Dialog", $"Present {view.GetType().Name}");
        stage.Content = view;
        stage.Visibility = Visibility.Visible;
        StateChanged?.Invoke(true);

        // 开场:淡入 + 轻微上浮
        view.RenderTransform = new TranslateTransform { Y = 12 };
        var duration = TransitionDuration();
        var opacity = new System.Windows.Media.Animation.DoubleAnimation(0, 1, duration)
        {
            EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
        };
        view.BeginAnimation(UIElement.OpacityProperty, opacity);
        if (view.RenderTransform is TranslateTransform translate)
        {
            var slide = new System.Windows.Media.Animation.DoubleAnimation(12, 0, duration)
            {
                EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut },
            };
            translate.BeginAnimation(TranslateTransform.YProperty, slide);
        }
    }

    private void CloseCore(ContentPresenter stage, object? result, TaskCompletionSource<object?> source)
    {
        _activeView = null;
        stage.Content = null;
        stage.Visibility = Visibility.Collapsed;
        StateChanged?.Invoke(false);
        source.TrySetResult(result);

        if (_pending.Count == 0 || _activeView is not null)
            return;

        var (nextView, nextSource) = _pending.Dequeue();
        Present(stage, nextView, nextSource);
    }

    /// <summary>窗口关闭时终结所有等待中的对话框,防止任务悬空。</summary>
    public void AbortAll()
    {
        var stage = _stage;
        _pending.Clear();
        if (_activeView is not null && stage is not null)
        {
            stage.Content = null;
            stage.Visibility = Visibility.Collapsed;
            _activeView = null;
        }
        StateChanged?.Invoke(false);
    }

    internal void RequestCancelActive(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 背板点击(见 MainWindow):取消当前对话框
        // TODO: 取消请求只对当前活动面板有效,等待中的面板在展示后需要用户自行取消。
        if (_activeView is DialogPanel panel)
            panel.RequestCancel();
    }

    private static Duration TransitionDuration()
    {
        if (Application.Current.TryFindResource("Dur.Dialog") is Duration d && d.HasTimeSpan)
            return d;
        return new Duration(TimeSpan.FromMilliseconds(180));
    }
}
