using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace SL.App.Pages;

/// <summary>应用日志页:侧选文件、关键词过滤与尾部滚动查看。</summary>
public sealed partial class AppLogPage : Page
{
    private readonly AppLogVM _vm = new();

    public AppLogPage()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm.RefreshData();
    }

    private void OnKeywordChanged(object sender, TextChangedEventArgs e)
        => _vm.Keyword = KeywordBox.Text;
}