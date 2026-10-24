using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class GlobalPrefsPage : Page
{
    public GlobalPrefsPage() => Content = new PlaceholderView("st.global",
        "全局偏好:窗口尺寸、代理与镜像源、界面语言、个人目录位置。");
}
