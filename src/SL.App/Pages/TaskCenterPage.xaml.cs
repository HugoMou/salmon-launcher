using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SL.App.Infrastructure;

namespace SL.App.Pages;

/// <summary>任务中心页:直接绑定组合根中的 TaskCenterVM(引擎事件已接入)。</summary>
public sealed partial class TaskCenterPage : Page
{
    public TaskCenterPage()
    {
        InitializeComponent();
        Root.DataContext = AppServices.TaskCenter;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        AppServices.TaskCenter.RefreshData();
    }
}