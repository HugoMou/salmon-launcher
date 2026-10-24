using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace SL.App.Pages;

/// <summary>崩溃诊断页。</summary>
public sealed partial class CrashDiagPage : Page
{
    private readonly CrashDiagVM _vm = new();

    public CrashDiagPage()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _vm.RefreshData();
    }
}