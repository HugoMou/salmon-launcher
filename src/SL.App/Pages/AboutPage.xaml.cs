using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SL.App.Infrastructure;
using SL.Core.Boot;

namespace SL.App.Pages;

/// <summary>关于页:版本、运行环境、能力概览与协议入口。</summary>
public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        VersionText.Text = $"版本 {Runtime.AppVersion} · Windows 桌面版";
        VersionRowText.Text = Runtime.AppVersion;
        RuntimeText.Text = $"WinUI 3 / Windows App SDK · .NET {Environment.Version} · {Environment.OSVersion.VersionString}";
        RootText.Text = Runtime.Root;
        SyncTerms();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SyncTerms();
    }

    private void SyncTerms()
        => TermsStateText.Text = AppServices.Settings.Current.TermsAccepted
            ? "已阅读并同意用户协议"
            : "尚未同意 · 前往「用户协议」页面确认";

    private void OnOpenRoot(object sender, RoutedEventArgs e) => OpenPath(Runtime.Root);

    private void OnOpenLogs(object sender, RoutedEventArgs e) => OpenPath(Runtime.LogDir);

    private void OnOpenTerms(object sender, RoutedEventArgs e) => AppServices.Nav.Navigate("st.terms");

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch
        {
            // 打开资源管理器失败时保持静默
        }
    }
}
