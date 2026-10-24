using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class InstanceConfigPage : Page
{
    public InstanceConfigPage() => Content = new PlaceholderView("inst.cfg",
        "实例设置:JVM 参数、内存分配、分辨率与启动前命令。");
}
