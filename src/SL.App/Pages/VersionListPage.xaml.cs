using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace SL.App.Pages;

/// <summary>游戏版本页:清单筛选与选择。</summary>
public sealed partial class VersionListPage : Page
{
    private readonly VersionListVM _vm = new();

    public VersionListPage()
    {
        InitializeComponent();
        DataContext = _vm;
        TypeRelease.IsChecked = true;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm.RefreshData();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
        => _vm.Keyword = SearchBox.Text;

    private void OnTypeChecked(object sender, RoutedEventArgs e)
        => _vm.TypeIndex = ReferenceEquals(sender, TypeRelease) ? 1
            : ReferenceEquals(sender, TypeSnapshot) ? 2
            : ReferenceEquals(sender, TypeAncient) ? 3
            : 0;

    private void OnItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is VersionRow row)
            _vm.Selected = row;
    }
}