using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class AboutPage : Page
{
    public AboutPage() => Content = new PlaceholderView("st.about",
        "关于:版本信息、开源许可与致谢。");
}
