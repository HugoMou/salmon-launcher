using System.ComponentModel;
using System.Windows.Controls;

namespace SL.Pres.Pages;

/// <summary>应用日志页(逻辑见 AppLogVM;按回车/刷新键重读)。</summary>
public partial class AppLogView : UserControl
{
    public AppLogView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (DataContext is AppLogVM vm)
                vm.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(AppLogVM.FilterText))
                        vm.Refresh();
                };
        };
    }
}
