using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SL.Pres.Controls;

/// <summary>下钻/返回的方向。</summary>
public enum TransitionKind
{
    Forward,
    Back,
}

/// <summary>
/// 页面过渡宿主:页面栈导航的进入/退出动画由此统一协调(状态机),
/// 页面自身不包含任何过渡逻辑。
/// - 内容替换时:旧页退出(淡出 + 平移),新页进入(反向平移 + 淡入);
/// - 方向由 Direction 决定(下钻向右出/返回向左出);
/// - 过渡中被再次切换会立即收敛上一次,避免叠加错乱;
/// - 过渡期间 ghost 旧页不接收交互。
/// </summary>
public class TransitionHost : Control
{
    public static readonly DependencyProperty ContentProperty = DependencyProperty.Register(
        nameof(Content), typeof(object), typeof(TransitionHost),
        new FrameworkPropertyMetadata(null,
            FrameworkPropertyMetadataOptions.AffectsMeasure, OnContentChanged));

    public object? Content
    {
        get => GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public static readonly DependencyProperty DirectionProperty = DependencyProperty.Register(
        nameof(Direction), typeof(TransitionKind), typeof(TransitionHost),
        new FrameworkPropertyMetadata(TransitionKind.Forward));

    public TransitionKind Direction
    {
        get => (TransitionKind)GetValue(DirectionProperty);
        set => SetValue(DirectionProperty, value);
    }

    private ContentPresenter? _current;
    private ContentPresenter? _ghost;
    private bool _busy;
    private int _generation;

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _current = GetTemplateChild("PART_Current") as ContentPresenter;
        _ghost = GetTemplateChild("PART_Ghost") as ContentPresenter;

        if (_current is not null)
        {
            _current.RenderTransform = new TranslateTransform();
            _current.Content = Content;
        }
        if (_ghost is not null)
        {
            _ghost.RenderTransform = new TranslateTransform();
            _ghost.IsHitTestVisible = false;
        }
    }

    private static void OnContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var host = (TransitionHost)d;
        host.PlayTransition(e.OldValue, e.NewValue);
    }

    private void PlayTransition(object? oldContent, object? newContent)
    {
        if (_current is null || _ghost is null)
            return; // 模板未应用:OnApplyTemplate 会同步初始呈现

        if (_busy)
            FinalizeTransition(); // 收敛上一次

        var hadOld = oldContent is not null;
        var generation = ++_generation;
        _busy = true;

        _ghost.Content = oldContent;
        _current.Content = newContent;

        if (!hadOld)
        {
            // 首屏:仅播放进入
            AnimateOpacity(_current, 0, 1);
            AnimateSlide(_current, Direction == TransitionKind.Back ? -28 : 28, 0,
                () => FinalizeIfCurrent(generation));
            return;
        }

        var forward = Direction == TransitionKind.Forward;
        var enterFrom = forward ? 28 : -28;
        var exitTo = forward ? -28 : 28;

        // 旧页退出
        AnimateOpacity(_ghost, 1, 0);
        AnimateSlide(_ghost, 0, exitTo, null);

        // 新页进入(完成回调负责收敛)
        AnimateOpacity(_current, 0, 1);
        AnimateSlide(_current, enterFrom, 0, () => FinalizeIfCurrent(generation));
    }

    private void FinalizeIfCurrent(int generation)
    {
        if (generation != _generation)
            return; // 已被更新的过渡收敛
        FinalizeTransition();
    }

    /// <summary>把过渡收敛到静止终态(同时作为立即终止过渡的手段使用)</summary>
    private void FinalizeTransition()
    {
        _generation++;
        _busy = false;

        if (_current is not null)
        {
            ClearAnimations(_current);
            SetSlide(_current, 0);
            _current.Opacity = 1;
        }

        if (_ghost is not null)
        {
            ClearAnimations(_ghost);
            SetSlide(_ghost, 0);
            _ghost.Opacity = 1;
            _ghost.Content = null;
        }
    }

    // ============================== 动画工具 ==============================

    /// <summary>过渡时长令牌(ThemeTokens);缺失时回退 220ms</summary>
    private static double TransitionMs
    {
        get
        {
            if (Application.Current?.TryFindResource("Dur.Trans") is Duration d && d.HasTimeSpan)
                return d.TimeSpan.TotalMilliseconds;
            return 220;
        }
    }

    private static void AnimateOpacity(FrameworkElement target, double from, double to)
    {
        var anim = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(TransitionMs)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        target.BeginAnimation(FrameworkElement.OpacityProperty, anim);
    }

    private static void AnimateSlide(FrameworkElement target, double from, double to, Action? done)
    {
        if (target.RenderTransform is not TranslateTransform translate)
        {
            translate = new TranslateTransform();
            target.RenderTransform = translate;
        }

        var anim = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(TransitionMs)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        if (done is not null)
            anim.Completed += (_, _) => done();

        translate.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    private static void SetSlide(FrameworkElement target, double x)
    {
        if (target.RenderTransform is TranslateTransform translate)
            translate.X = x;
    }

    private static void ClearAnimations(FrameworkElement target)
    {
        target.BeginAnimation(FrameworkElement.OpacityProperty, null);
        if (target.RenderTransform is TranslateTransform translate)
            translate.BeginAnimation(TranslateTransform.XProperty, null);
    }
}
