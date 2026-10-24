using Microsoft.UI.Xaml.Controls;
using SL.App.Infrastructure;
using SL.App.Notify;

namespace SL.App.Navigation;

/// <summary>基于 Frame 的路由导航:Navigate 按 Key 跳转,Frame 日志提供返回栈。</summary>
public sealed class NavService
{
    private Frame _frame = null!;

    public string CurrentKey { get; private set; } = "launch.home";

    /// <summary>当前页变化(含返回)时触发,用于同步 NavigationView 选中态与标题。</summary>
    public event Action? CurrentChanged;

    public void Attach(Frame frame)
    {
        _frame = frame;
        _frame.Navigated += OnNavigated;
        Navigate(CurrentKey);
    }

    private void OnNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        if (e.Parameter is string key && PageCatalog.All.ContainsKey(key) && CurrentKey != key)
        {
            CurrentKey = key;
            CurrentChanged?.Invoke();
        }
    }

    public void Navigate(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;
        if (!PageCatalog.All.TryGetValue(key, out var spec))
        {
            AppServices.Notify.Publish(NotifyKind.Error, $"未注册页面:{key}");
            return;
        }
        if (key == CurrentKey && _frame.Content is not null && _frame.CurrentSourcePageType == spec.PageType)
            return;
        _frame.Navigate(spec.PageType, key);
    }

    public bool CanBack => _frame.CanGoBack;

    public void Back()
    {
        if (_frame.CanGoBack)
            _frame.GoBack();
    }
}
