using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class LoaderListPage : Page
{
    public LoaderListPage() => Content = new PlaceholderView("dl.loaders",
        "加载器选择:Fabric / Forge / NeoForge / Quilt 与对应版本匹配。");
}
