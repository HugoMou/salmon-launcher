using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class ModrinthPacksPage : Page
{
    public ModrinthPacksPage() => Content = new PlaceholderView("dl.packs",
        "资源包仓库:Modrinth resource-packs 分类检索与安装。");
}
