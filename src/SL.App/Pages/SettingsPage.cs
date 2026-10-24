using Microsoft.UI.Xaml.Controls;

namespace SL.App.Pages;

public sealed class SettingsPage : Page
{
    public SettingsPage() => Content = new PlaceholderView("st.launch",
        "外观与设置:深浅色(含跟随系统)、4 套配色、5 种强调色与下载并发设置(Settings.Changed 已联动 ThemeManager)。");
}
