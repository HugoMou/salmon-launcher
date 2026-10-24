using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class InstancePacksPage : Page
{
    public InstancePacksPage() => Content = new PlaceholderView("inst.packs",
        "资源包管理:实例内资源包列表与导入。");
}
