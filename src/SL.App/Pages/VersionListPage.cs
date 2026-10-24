using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class VersionListPage : Page
{
    public VersionListPage() => Content = new PlaceholderView("dl.game",
        "游戏版本:Mojang 版本清单(官方/BMCLAPI 双源)按类型筛选与搜索。");
}
