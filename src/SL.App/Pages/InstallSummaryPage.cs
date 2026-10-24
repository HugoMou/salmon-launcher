using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class InstallSummaryPage : Page
{
    public InstallSummaryPage() => Content = new PlaceholderView("dl.install",
        "安装概览:所选版本、加载器与组件清单确认,提交安装流水线。");
}
