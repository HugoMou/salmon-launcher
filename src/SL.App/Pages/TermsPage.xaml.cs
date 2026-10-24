using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SL.App.Infrastructure;
using SL.App.Notify;

namespace SL.App.Pages;

/// <summary>用户协议与免责声明。</summary>
public sealed partial class TermsPage : Page
{
    public TermsPage()
    {
        InitializeComponent();
        SyncState();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SyncState();
    }

    private void SyncState()
    {
        var accepted = AppServices.Settings.Current.TermsAccepted;
        AcceptStateText.Text = accepted
            ? "你已同意本协议。协议内容更新后会再次提示确认。"
            : "你还没有同意本协议。同意后才能继续使用启动、下载与联机功能。";
        AcceptButton.Visibility = accepted ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnAccept(object sender, RoutedEventArgs e)
    {
        AppServices.Settings.Update(c => c.TermsAccepted = true);
        SyncState();
        AppServices.Notify.Publish(NotifyKind.Success, "已同意用户协议");
    }
}
