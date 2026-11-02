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
using Microsoft.Web.WebView2.Core;
using SL.App.Infrastructure;
using SL.App.Localization;
using SL.App.Notify;
using SL.Core.Account;
using SL.Core.Logging;

namespace SL.App.Dialogs;

/// <summary>
/// Built-in browser premium login: WebView2 opens the Microsoft authorization page (PKCE); the moment
/// login redirects to oauth20_desktop.srf, the authorization code is captured in place and the whole flow continues, with no copy-paste.
/// </summary>
public sealed partial class BrowserLoginDialog : ContentDialog
{
    private readonly CancellationTokenSource _cts = new();
    private readonly DispatcherQueue _ui;
    private string _verifier = "";
    private bool _handled;
    private bool _closed;

    public BrowserLoginDialog()
    {
        InitializeComponent();
        _ui = DispatcherQueue.GetForCurrentThread();
        Opened += async (_, _) =>
        {
            Loc.ApplyTo(this);
            FitToHost();
            await StartAsync();
        };
        Closed += (_, _) =>
        {
            _closed = true;
            _cts.Cancel();
        };
    }

    /// <summary>Size the content to the window: the login page needs more room than the dialog template default allows.</summary>
    private void FitToHost()
    {
        if (XamlRoot is not { } root)
            return;
        var size = root.Size;
        Body.Width = Math.Clamp(size.Width - 220, 480, 1040);
        Body.Height = Math.Clamp(size.Height - 300, 320, 720);
    }

    private async Task StartAsync()
    {
        try
        {
            SetStatus(Loc.T("正在打开微软登录页;登录完成后本窗口会自动继续…"), working: true);
            var req = MsAuth.BeginInteractive();
            _verifier = req.Verifier;
            Web.CoreWebView2Initialized += OnWebInitialized;
            Web.NavigationStarting += OnNavigationStarting;
            Web.Source = new Uri(req.Url);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            AppLog.Error("Account", ex);
            SetStatus(Loc.T("无法打开内置浏览器:") + ex.Message, working: false, error: true);
        }
    }

    private void OnWebInitialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
    {
        if (args.Exception is null)
            return;
        _ui.TryEnqueue(() => SetStatus(
            Loc.T("WebView2 运行时初始化失败") + "(" + args.Exception.Message + ");"
            + Loc.T("请改用设备码登录,或复制授权链接到系统浏览器打开。"),
            working: false, error: true));
    }

    private void OnNavigationStarting(WebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        var url = args.Uri ?? "";
        if (!url.StartsWith("https://login.live.com/oauth20_desktop.srf", StringComparison.OrdinalIgnoreCase))
            return;
        args.Cancel = true;
        if (_handled)
            return;
        _handled = true;
        _ = FinishAsync(url);
    }

    private async Task FinishAsync(string callbackUrl)
    {
        try
        {
            _ui.TryEnqueue(() => SetStatus(Loc.T("授权完成,正在换取令牌并读取档案…"), working: true));
            var code = MsAuth.ExtractAuthorizationCode(callbackUrl);
            var session = await MsAuth.CompleteInteractiveAsync(code, _verifier,
                step => _ui.TryEnqueue(() => SetStatus(step, working: true)), _cts.Token).ConfigureAwait(true);

            var entry = AccountStore.UpsertMsa(session);
            AppServices.Settings.Update(c =>
            {
                c.AccountType = "msa";
                c.AccountName = session.Name;
                c.ActiveAccountId = entry.Id;
            });
            AppLog.Info("Account", $"正版登录成功(内置浏览器):{session.Name}");
            AppServices.Notify.Publish(NotifyKind.Success, string.Format(Loc.T("正版登录成功:{0}"), session.Name));
            Hide();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (_closed)
                return;
            AppLog.Error("Account", ex);
            _handled = false;
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
}