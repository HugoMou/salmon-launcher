using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class VoxLinkPage : Page
{
    public VoxLinkPage() => Content = new PlaceholderView("tools.online",
        "联机(VoxLink):隧道创建、房间管理与延迟显示。");
}
