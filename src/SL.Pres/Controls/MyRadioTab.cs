using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace SL.Pres.Controls;

/// <summary>
/// 启动器专用单选控件:标题栏标签、左侧导航、筛选 Chip 均由此派生(互斥单选语义)。
/// 视觉状态:悬停 / 选中 / 禁用,可选左侧指示条(Led)。
/// </summary>
public class MyRadioTab : RadioButton
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(CornerRadius), typeof(MyRadioTab),
        new FrameworkPropertyMetadata(new CornerRadius(7),
            FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>可选的图标字形（Segoe Fluent Icons / Segoe MDL2 Assets）。</summary>
    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(MyRadioTab), new PropertyMetadata(""));

    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly DependencyProperty HoverBackgroundProperty = DependencyProperty.Register(
        nameof(HoverBackground), typeof(Brush), typeof(MyRadioTab), new PropertyMetadata(null));

    public Brush HoverBackground
    {
        get => (Brush)GetValue(HoverBackgroundProperty);
        set => SetValue(HoverBackgroundProperty, value);
    }

    public static readonly DependencyProperty CheckedBackgroundProperty = DependencyProperty.Register(
        nameof(CheckedBackground), typeof(Brush), typeof(MyRadioTab), new PropertyMetadata(null));

    public Brush CheckedBackground
    {
        get => (Brush)GetValue(CheckedBackgroundProperty);
        set => SetValue(CheckedBackgroundProperty, value);
    }

    public static readonly DependencyProperty CheckedForegroundProperty = DependencyProperty.Register(
        nameof(CheckedForeground), typeof(Brush), typeof(MyRadioTab), new PropertyMetadata(null));

    public Brush CheckedForeground
    {
        get => (Brush)GetValue(CheckedForegroundProperty);
        set => SetValue(CheckedForegroundProperty, value);
    }

    /// <summary>是否显示左侧指示条(左侧导航样式用)</summary>
    public static readonly DependencyProperty ShowLedProperty = DependencyProperty.Register(
        nameof(ShowLed), typeof(bool), typeof(MyRadioTab), new PropertyMetadata(false));

    public bool ShowLed
    {
        get => (bool)GetValue(ShowLedProperty);
        set => SetValue(ShowLedProperty, value);
    }

    public static readonly DependencyProperty DisabledOpacityProperty = DependencyProperty.Register(
        nameof(DisabledOpacity), typeof(double), typeof(MyRadioTab), new PropertyMetadata(0.45));

    public double DisabledOpacity
    {
        get => (double)GetValue(DisabledOpacityProperty);
        set => SetValue(DisabledOpacityProperty, value);
    }
}
