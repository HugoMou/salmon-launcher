using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SL.Core.Transfers;

namespace SL.Pres.Controls;

/// <summary>
/// 可嵌入任意页面的后台任务进度卡:
/// - 绑定 TransferSnapshot 的投影(TaskRowVM);
/// - 显示名称、状态徽章、进度条、已传/总量、速度、预计剩余时间;
/// - 按状态显隐 暂停/继续/取消/重试 操作;
/// - 状态色与文案由模板内的状态触发器统一驱动(不散落在页面)。
/// </summary>
public class TaskCardControl : Control
{
    static TaskCardControl()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(TaskCardControl),
            new FrameworkPropertyMetadata(typeof(TaskCardControl)));
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(TaskCardControl), new PropertyMetadata(""));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public static readonly DependencyProperty RowStateProperty = DependencyProperty.Register(
        nameof(RowState), typeof(TransferState), typeof(TaskCardControl),
        new FrameworkPropertyMetadata(TransferState.Queued, FrameworkPropertyMetadataOptions.AffectsRender));

    public TransferState RowState
    {
        get => (TransferState)GetValue(RowStateProperty);
        set => SetValue(RowStateProperty, value);
    }

    public static readonly DependencyProperty StateTextProperty = DependencyProperty.Register(
        nameof(StateText), typeof(string), typeof(TaskCardControl), new PropertyMetadata(""));

    public string StateText { get => (string)GetValue(StateTextProperty); set => SetValue(StateTextProperty, value); }

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(TaskCardControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    public static readonly DependencyProperty BytesTextProperty = DependencyProperty.Register(
        nameof(BytesText), typeof(string), typeof(TaskCardControl), new PropertyMetadata(""));

    public string BytesText { get => (string)GetValue(BytesTextProperty); set => SetValue(BytesTextProperty, value); }

    public static readonly DependencyProperty SpeedTextProperty = DependencyProperty.Register(
        nameof(SpeedText), typeof(string), typeof(TaskCardControl), new PropertyMetadata(""));

    public string SpeedText { get => (string)GetValue(SpeedTextProperty); set => SetValue(SpeedTextProperty, value); }

    public static readonly DependencyProperty RemainTextProperty = DependencyProperty.Register(
        nameof(RemainText), typeof(string), typeof(TaskCardControl), new PropertyMetadata(""));

    public string RemainText { get => (string)GetValue(RemainTextProperty); set => SetValue(RemainTextProperty, value); }

    public static readonly DependencyProperty ErrorTextProperty = DependencyProperty.Register(
        nameof(ErrorText), typeof(string), typeof(TaskCardControl), new PropertyMetadata(""));

    public string ErrorText { get => (string)GetValue(ErrorTextProperty); set => SetValue(ErrorTextProperty, value); }

    public static readonly DependencyProperty PauseCommandProperty = DependencyProperty.Register(
        nameof(PauseCommand), typeof(ICommand), typeof(TaskCardControl), new PropertyMetadata(null));

    public ICommand PauseCommand { get => (ICommand)GetValue(PauseCommandProperty); set => SetValue(PauseCommandProperty, value); }

    public static readonly DependencyProperty ResumeCommandProperty = DependencyProperty.Register(
        nameof(ResumeCommand), typeof(ICommand), typeof(TaskCardControl), new PropertyMetadata(null));

    public ICommand ResumeCommand { get => (ICommand)GetValue(ResumeCommandProperty); set => SetValue(ResumeCommandProperty, value); }

    public static readonly DependencyProperty CancelCommandProperty = DependencyProperty.Register(
        nameof(CancelCommand), typeof(ICommand), typeof(TaskCardControl), new PropertyMetadata(null));

    public ICommand CancelCommand { get => (ICommand)GetValue(CancelCommandProperty); set => SetValue(CancelCommandProperty, value); }

    public static readonly DependencyProperty RetryCommandProperty = DependencyProperty.Register(
        nameof(RetryCommand), typeof(ICommand), typeof(TaskCardControl), new PropertyMetadata(null));

    public ICommand RetryCommand { get => (ICommand)GetValue(RetryCommandProperty); set => SetValue(RetryCommandProperty, value); }
}
