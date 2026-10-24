using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class InstanceShadersPage : Page
{
    public InstanceShadersPage() => Content = new PlaceholderView("inst.shaders",
        "光影管理:实例内光影列表与 shaderpacks 目录操作。");
}
