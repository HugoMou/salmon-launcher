using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class InstanceSavesPage : Page
{
    public InstanceSavesPage() => Content = new PlaceholderView("inst.saves",
        "存档管理:世界列表、备份/恢复与 level.dat 信息展示。");
}
