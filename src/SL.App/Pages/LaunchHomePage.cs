using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class LaunchHomePage : Page
{
    public LaunchHomePage() => Content = new PlaceholderView("launch.home",
        "启动面板:账户选择、版本选择、启动按钮与全局状态区(Java 就绪/任务进行数)。");
}
