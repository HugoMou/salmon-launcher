using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SL.App.Navigation;

namespace SL.App.Pages;

/// <summary>占位页:所有路由共用,从导航参数 Key 查 PageCatalog 显示标题与说明。</summary>
public sealed partial class PlaceholderPage : Page
{
    public PlaceholderPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not string key || !PageCatalog.All.TryGetValue(key, out var spec))
            return;
        TitleText.Text = spec.Title;
        HintText.Text = spec.Hint;
        KeyText.Text = $"路由 {key}";
    }
}
