using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;

namespace SL.App.Pages;

/// <summary>仓库页:模组 / 光影 / 资源包三类共用同一套检索与安装逻辑,分类由路由 key 决定。</summary>
public sealed partial class RepoPage : Page
{
    private readonly RepoVM _vm = new();

    public RepoPage()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string key)
            _vm.Configure(key);
        _vm.RefreshData();
    }

    private void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
            return;
        _vm.Keyword = SearchBox.Text;
        _vm.SearchCommand.Execute(null);
    }
}