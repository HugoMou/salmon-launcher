using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class CrashDiagPage : Page
{
    public CrashDiagPage() => Content = new PlaceholderView("tools.diag",
        "崩溃诊断:崩溃报告解析、特征匹配与修复建议。");
}
