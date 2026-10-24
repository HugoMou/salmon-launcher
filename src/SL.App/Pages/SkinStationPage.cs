using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class SkinStationPage : Page
{
    public SkinStationPage() => Content = new PlaceholderView("tools.skins",
        "皮肤站:authlib-injector 源接入、皮肤上传与预览。");
}
