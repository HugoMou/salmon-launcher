// SalmonLauncher (SL) — a Minecraft Java edition launcher
// Copyright (C) 2026 Hugo Mou
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
// PARTICULAR PURPOSE. See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with
// this program. If not, see <https://www.gnu.org/licenses/>.
//
// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SL.Core.Account;
using SL.Core.Logging;
using SL.App.Infrastructure;
using SL.App.Notify;
using SL.App.Localization;
using Windows.ApplicationModel.DataTransfer;

namespace SL.App.Dialogs;

/// <summary>Device code login: shows user_code in large text to copy into microsoft.com/link; the dialog polls until authorization completes and the whole flow continues.</summary>
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
            SetStatus(Loc.T("正在向微软申请设备码…"), working: true);
            var (userCode, deviceCode, link, interval) = await MsAuth.StartAsync(_cts.Token);
            if (_closed)
                return;
            _link = link;
            CodeText.Text = string.Join(' ', userCode.Select(c => c.ToString()));
            CodeText.Tag = userCode;
            CopyButton.IsEnabled = true;
            OpenButton.IsEnabled = true;
            SetStatus(Loc.T("等待浏览器授权中;完成后本窗口会自动继续…"), working: true);

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
            AppServices.Notify.Publish(NotifyKind.Success, string.Format(Loc.T("正版登录成功:{0}"), session.Name));
            Hide();
        }
        catch (OperationCanceledException)
        {
            // User closed the dialog: exit silently
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
        AppServices.Notify.Publish(NotifyKind.Success, Loc.T("设备码已复制,请粘贴到浏览器登录页"));
    }

    private async void OnOpen(object sender, RoutedEventArgs e)
        => await Windows.System.Launcher.LaunchUriAsync(new Uri(_link));
}
