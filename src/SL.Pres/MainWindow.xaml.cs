using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SL.Pres.Controls;
using SL.Pres.Infrastructure;
using SL.Pres.Themes;

namespace SL.Pres;

public partial class MainWindow : Window
{
    private const double NavExpandedWidth = 240;
    private const double NavCollapsedWidth = 56;

    private double _navWidth = NavExpandedWidth;
    private bool _mica;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = AppServices.Nav;

        AppServices.Dialogs.Attach(DialogStage);
        AppServices.Nav.SidebarToggleRequested += OnSidebarToggleRequested;
        AppServices.Dialogs.StateChanged += OnDialogStateChanged;
        ThemeManager.ThemeChanged += OnThemeChanged;
        StateChanged += OnWindowStateChanged;
        SourceInitialized += (_, _) => ApplyBackdrop();

        Loaded += (_, _) =>
        {
            if (NavCol.ActualWidth > 20 && NavCol.ActualWidth < 600)
                _navWidth = NavCol.ActualWidth;
        };

        Closed += (_, _) =>
        {
            AppServices.Nav.SidebarToggleRequested -= OnSidebarToggleRequested;
            AppServices.Dialogs.StateChanged -= OnDialogStateChanged;
            ThemeManager.ThemeChanged -= OnThemeChanged;
            AppServices.Dialogs.AbortAll();
        };
    }

    // ============================== 云母背景 ==============================

    private void ApplyBackdrop()
    {
        _mica = WindowBackdrop.TryApply(this, ThemeManager.IsDark);
        if (_mica)
        {
            // 窗口透明，由系统云母填充；根容器铺一层半透明着色保证文字可读
            Background = Brushes.Transparent;
            Root.SetResourceReference(Panel.BackgroundProperty, "Brush.WindowTint");
        }
    }

    private void OnThemeChanged()
    {
        WindowBackdrop.SetDark(this, ThemeManager.IsDark);
        if (_mica)
            Root.SetResourceReference(Panel.BackgroundProperty, "Brush.WindowTint");
    }

    // ============================== 侧栏折叠 ==============================

    private void OnSidebarToggleRequested()
    {
        var target = AppServices.Nav.IsLeftCollapsed ? NavCollapsedWidth : _navWidth;
        if (!AppServices.Nav.IsLeftCollapsed)
        {
            var actual = NavCol.ActualWidth;
            if (actual > 20)
                _navWidth = actual;
            target = _navWidth;
        }
        GridColumnAnimator.AnimateTo(NavCol, target, 180);
    }

    // ============================== 对话框层 ==============================

    private void OnDialogStateChanged(bool open)
    {
        DialogLayer.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        // 覆盖层 + 主内容禁用交互；标题栏保留可用
        NavHost.IsEnabled = !open;
        PageContent.IsEnabled = !open;

        if (open)
            DialogLayer.Focus();
    }

    private void DialogLayer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 点击背板 = 取消当前对话框
        if (ReferenceEquals(e.OriginalSource, DialogLayer))
        {
            AppServices.Dialogs.RequestCancelActive(sender, e);
            e.Handled = true;
        }
    }

    // ============================== 标题栏 ==============================

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed)
            return;

        if (e.ClickCount >= 2)
        {
            ToggleMaximize();
            return;
        }

        try
        {
            DragMove();
        }
        catch
        {
            // DragMove 在某些状态转换瞬间会抛出，忽略即可
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        Core.Logging.AppLog.Info("Win", $"WindowState → {WindowState}");
        UpdateMaximizeGlyph();
    }

    private void UpdateMaximizeGlyph()
        => BtnMaximize.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";

    private void Minimize_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
        => ToggleMaximize();

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
}
