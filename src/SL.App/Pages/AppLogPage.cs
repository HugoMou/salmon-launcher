using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class AppLogPage : Page
{
    public AppLogPage() => Content = new PlaceholderView("tools.log",
        "应用日志:实时滚动查看、级别过滤与导出。");
}
