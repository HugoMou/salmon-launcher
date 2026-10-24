using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace SL.App.Pages;

/// <summary>启动面板主页:大启动按钮、实例选择、账户状态与快捷入口。</summary>
public sealed partial class HomePage : Page
{
    private readonly HomeVM _vm = new();

    public HomePage()
    {
        InitializeComponent();
        Root.DataContext = _vm;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm.Refresh();
    }
}
