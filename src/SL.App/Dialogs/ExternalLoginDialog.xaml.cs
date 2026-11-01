// Salmon Launcher (SL) — a Minecraft Java edition launcher
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
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SL.App.Infrastructure;
using SL.App.Localization;
using SL.App.Notify;
using SL.Core.Logging;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace SL.App.Dialogs;

/// <summary>
/// 外部浏览器登录:直接唤起系统默认浏览器打开微软授权页,登录完成后
/// 让用户把回跳链接粘回来换取会话(适用于内置浏览器不可用的环境)。
/// </summary>
public sealed partial class ExternalLoginDialog : ContentDialog
{
    private readonly string _url;

    public ExternalLoginDialog(string url)
    {
        InitializeComponent();
        _url = url;
        Opened += async (_, _) =>
        {
            Loc.ApplyTo(this);
            await OpenBrowserAsync();
        };
        PrimaryButtonClick += OnPrimary;
    }

    /// <summary>用户填写的回跳链接(仅在点了「完成登录」时有值)。</summary>
    public string? CallbackUrl { get; private set; }

    private async Task OpenBrowserAsync()
    {
        try
        {
            if (await Launcher.LaunchUriAsync(new Uri(_url)))
            {
                HintText.Text = Loc.T("已把授权页交给系统浏览器;登录完成后把浏览器地址栏里的完整链接粘贴到上面。");
                CallbackBox.Focus(FocusState.Programmatic);
                return;
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Account", "唤起系统浏览器失败:" + ex.Message);
        }
        HintText.Text = Loc.T("没能唤起系统浏览器;可以点「复制授权链接」后自己打开浏览器。");
    }

    private async void OnOpen(object sender, RoutedEventArgs e) => await OpenBrowserAsync();

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(_url);
            Clipboard.SetContent(package);
            Clipboard.Flush();
            AppServices.Notify.Publish(NotifyKind.Success, Loc.T("授权链接已复制到剪贴板"));
        }
        catch (Exception ex)
        {
            AppLog.Warn("Account", "复制授权链接失败:" + ex.Message);
        }
    }

    private void OnPrimary(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var text = CallbackBox.Text?.Trim() ?? "";
        if (text.Length == 0)
        {
            args.Cancel = true;
            HintText.Text = Loc.T("先把浏览器地址栏里的完整回跳链接粘贴到上面,再点「完成登录」。");
            CallbackBox.Focus(FocusState.Programmatic);
            return;
        }
        CallbackUrl = text;
    }
}