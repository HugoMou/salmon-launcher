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
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SL.App.Localization;
using SL.App.Infrastructure;
using SL.Core.Boot;

namespace SL.App.Pages;

/// <summary>About page: version, runtime environment, capability overview, and policy entry points.</summary>
public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
        VersionText.Text = string.Format(Loc.T("版本 {0} · Windows 桌面版"), Runtime.AppVersion);
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
            ? Loc.T("已阅读并同意用户协议")
            : Loc.T("尚未同意 · 前往「用户协议」页面确认");

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
            // Stay silent if opening Explorer fails
        }
    }
}
