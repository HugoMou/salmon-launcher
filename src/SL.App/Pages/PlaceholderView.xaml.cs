using Microsoft.UI.Xaml.Controls;
using SL.App.Navigation;

namespace SL.App.Pages;

/// <summary>占位视图:标题取自 PageCatalog,内容说明路由与后续计划。</summary>
public sealed partial class PlaceholderView : UserControl
{
    public PlaceholderView(string key, string hint)
    {
        InitializeComponent();
        var spec = PageCatalog.Get(key);
        TitleText.Text = spec.Title;
        HintText.Text = hint;
        KeyText.Text = $"路由 {key} · {spec.PageType.Name}";
    }
}
