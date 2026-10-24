using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class InstanceModsPage : Page
{
    public InstanceModsPage() => Content = new PlaceholderView("inst.mods",
        "模组管理:启停、更新、删除与冲突提示。");
}
