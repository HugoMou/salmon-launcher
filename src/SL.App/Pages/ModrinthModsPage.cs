using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class ModrinthModsPage : Page
{
    public ModrinthModsPage() => Content = new PlaceholderView("dl.pack",
        "模组仓库:Modrinth 搜索、项目卡片与版本下载(图像走 ImageCacheService)。");
}
