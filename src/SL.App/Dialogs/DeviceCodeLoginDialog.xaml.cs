using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SL.Core.Account;
using SL.Core.Logging;
using SL.App.Infrastructure;
using SL.App.Notify;
using Windows.ApplicationModel.DataTransfer;

namespace SL.App.Dialogs;

/// <summary>设备码登录:大字展示 user_code,复制后到 microsoft.com/link 粘贴;对话框内轮询至授权完成并续跑全链路。</summary>
public sealed partial class DeviceCodeLoginDialog : ContentDialog
{
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherQueue _ui;
    private string _link = "https://www.microsoft.com/link";
    private bool _closed;

    public DeviceCodeLoginDialog()
    {
        InitializeComponent();
        _ui = DispatcherQueue.GetForCurrentThread();
        Opened += async (_, _) => await RunAsync();
        Closed += (_, _) =>
        {
            _closed = true;
            _cts.Cancel();
        };
    }

    private async Task RunAsync()
    {
        try
        {
            SetStatus("正在向微软申请设备码…", working: true);
            var (userCode, deviceCode, link, interval) = await MsAuth.StartAsync(_cts.Token);
            if (_closed)
                return;
            _link = link;
            CodeText.Text = string.Join(' ', userCode.Select(c => c.ToString()));
            CodeText.Tag = userCode;
            CopyButton.IsEnabled = true;
            OpenButton.IsEnabled = true;
            SetStatus("等待浏览器授权中;完成后本窗口会自动继续…", working: true);

            var session = await MsAuth.LoginAsync(deviceCode, interval, 900,
                step => _ui.TryEnqueue(() => SetStatus(step, working: true)), _cts.Token);

            var entry = AccountStore.UpsertMsa(session);
            AppServices.Settings.Update(c =>
            {
                c.AccountType = "msa";
                c.AccountName = session.Name;
                c.ActiveAccountId = entry.Id;
            });
            AppLog.Info("Account", $"正版登录成功:{session.Name}");
            AppServices.Notify.Publish(NotifyKind.Success, $"正版登录成功:{session.Name}");
            Hide();
        }
        catch (OperationCanceledException)
        {
            // 用户关闭对话框:静默退出
        }
        catch (Exception ex)
        {
            if (_closed)
                return;
            AppLog.Error("Account", ex);
            SetStatus(ex.Message, working: false, error: true);
        }
    }

    private void SetStatus(string text, bool working, bool error = false)
    {
        BusyRing.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
        BusyRing.IsActive = working;
        ErrorIcon.Visibility = error ? Visibility.Visible : Visibility.Collapsed;
        StepText.Text = text;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (CodeText.Tag is not string code || code.Length == 0)
            return;
        var pkg = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        pkg.SetText(code);
        Clipboard.SetContent(pkg);
        Clipboard.Flush();
        AppServices.Notify.Publish(NotifyKind.Success, "设备码已复制,请粘贴到浏览器登录页");
    }

    private async void OnOpen(object sender, RoutedEventArgs e)
        => await Windows.System.Launcher.LaunchUriAsync(new Uri(_link));
}
