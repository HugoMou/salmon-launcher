using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SL.Pres.Pages;

/// <summary>Modrinth 搜索页绑定骨架(逻辑见 ModrinthVM)。</summary>
public partial class ModrinthView : UserControl
{
    public ModrinthView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (SearchBox is { } box)
                box.KeyDown += (_, e) =>
                {
                    if (e.Key is System.Windows.Input.Key.Enter && DataContext is ModrinthVM vm
                        && vm.SearchCommand.CanExecute(null))
                        vm.SearchCommand.Execute(null);
                };
        };
    }
}
