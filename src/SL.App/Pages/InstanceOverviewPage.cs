using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class InstanceOverviewPage : Page
{
    public InstanceOverviewPage() => Content = new PlaceholderView("inst.overview",
        "实例概览:图标、版本信息、快捷启动与操作入口。");
}
