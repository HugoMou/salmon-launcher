using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SL.Pres.Controls;

/// <summary>
/// 启动器专用按钮:
/// - 动作在「鼠标释放」时触发(基类 ButtonBase 的默认语义,配合按下/按下视觉状态);
/// - 通过视觉状态 DP(Hover/Pressed/禁用透明度)与模板组合出多种音调;
/// - 额外视觉状态由各音调样式在 Themes/Styles.Controls.xaml 中定义。
/// </summary>
public class MyButton : Button
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(MyButton),
        new FrameworkPropertyMetadata(new CornerRadius(7),
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly DependencyProperty HoverBackgroundProperty = DependencyProperty.Register(
        nameof(HoverBackground), typeof(Brush), typeof(MyButton), new PropertyMetadata(null));

    public Brush HoverBackground
    {
        get => (Brush)GetValue(HoverBackgroundProperty);
        set => SetValue(HoverBackgroundProperty, value);
    }

    public static readonly DependencyProperty HoverBorderBrushProperty = DependencyProperty.Register(
        nameof(HoverBorderBrush), typeof(Brush), typeof(MyButton), new PropertyMetadata(null));

    public Brush HoverBorderBrush
    {
        get => (Brush)GetValue(HoverBorderBrushProperty);
        set => SetValue(HoverBorderBrushProperty, value);
    }

    public static readonly DependencyProperty PressedBackgroundProperty = DependencyProperty.Register(
        nameof(PressedBackground), typeof(Brush), typeof(MyButton), new PropertyMetadata(null));

    public Brush PressedBackground
    {
        get => (Brush)GetValue(PressedBackgroundProperty);
        set => SetValue(PressedBackgroundProperty, value);
    }

    public static readonly DependencyProperty DisabledOpacityProperty = DependencyProperty.Register(
        nameof(DisabledOpacity), typeof(double), typeof(MyButton), new PropertyMetadata(0.45));

    public double DisabledOpacity
    {
        get => (double)GetValue(DisabledOpacityProperty);
        set => SetValue(DisabledOpacityProperty, value);
    }
}
