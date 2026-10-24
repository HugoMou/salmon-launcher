using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class InstanceLogsPage : Page
{
    public InstanceLogsPage() => Content = new PlaceholderView("inst.logs",
        "实例日志:最新日志/调试日志查看与检索。");
}
