using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class ModrinthShadersPage : Page
{
    public ModrinthShadersPage() => Content = new PlaceholderView("dl.shaders",
        "光影仓库:Modrinth shaders 分类检索与安装到当前实例。");
}
