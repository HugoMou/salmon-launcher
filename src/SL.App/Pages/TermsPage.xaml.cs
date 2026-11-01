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
using Microsoft.UI.Xaml.Navigation;
using SL.App.Infrastructure;
using SL.App.Localization;
using SL.App.Notify;
using SL.Core.Boot;

namespace SL.App.Pages;

/// <summary>用户协议与免责声明。</summary>
public sealed partial class TermsPage : Page
{
    public TermsPage()
    {
        InitializeComponent();
        SyncVersion();
        SyncState();
        Loaded += (_, _) =>
        {
            SyncVersion();
            Loc.Changed += SyncVersion;
        };
        Unloaded += (_, _) => Loc.Changed -= SyncVersion;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SyncState();
    }

    private void SyncVersion() => VersionLineText.Text = $"{Loc.T("版本")} {Runtime.AppVersion}";

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