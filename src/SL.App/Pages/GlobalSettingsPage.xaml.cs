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
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SL.Core.Boot;
using SL.App.Infrastructure;
using SL.App.Localization;

namespace SL.App.Pages;

/// <summary>全局偏好页:内存/窗口/JVM 参数/下载源与数据目录、Java 环境。</summary>
public sealed partial class GlobalSettingsPage : Page
{
    private bool _loading;

    public GlobalSettingsPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        LoadFromSettings();
        JavaEnv.Changed += OnJavaChanged;
        UpdateJavaText();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
        => JavaEnv.Changed -= OnJavaChanged;

    private void OnJavaChanged()
    {
        DispatcherQueue.TryEnqueue(UpdateJavaText);
    }

    private void UpdateJavaText()
        => JavaText.Text = string.IsNullOrWhiteSpace(JavaEnv.DetailText)
            ? JavaEnv.StatusText
            : JavaEnv.StatusText + " · " + JavaEnv.DetailText;

    private void LoadFromSettings()
    {
        _loading = true;
        var cfg = AppServices.Settings.Current;
        MemoryBox.Value = cfg.MemoryMb;
        WidthBox.Value = cfg.GameWidth;
        HeightBox.Value = cfg.GameHeight;
        JvmBox.Text = cfg.JvmExtraArgs;
        SourceAuto.IsChecked = cfg.DownloadSource != "bmclapi";
        SourceMirror.IsChecked = cfg.DownloadSource == "bmclapi";
        RootText.Text = Runtime.Root;
        _loading = false;
    }

    private void OnMemoryChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue))
            return;
        var value = (int)args.NewValue;
        AppServices.Settings.Update(c => c.MemoryMb = value);
    }

    private void OnWindowChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue))
            return;
        var width = double.IsNaN(WidthBox.Value) ? 854 : (int)WidthBox.Value;
        var height = double.IsNaN(HeightBox.Value) ? 480 : (int)HeightBox.Value;
        AppServices.Settings.Update(c =>
        {
            c.GameWidth = width;
            c.GameHeight = height;
        });
    }

    private void OnJvmChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        var text = JvmBox.Text.Trim();
        AppServices.Settings.Update(c => c.JvmExtraArgs = text);
    }

    private void OnSourceChecked(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;
        var source = ReferenceEquals(sender, SourceMirror) ? "bmclapi" : "auto";
        AppServices.Settings.Update(c => c.DownloadSource = source);
    }

    private void OnOpenRoot(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = Runtime.Root, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            SL.Core.Logging.AppLog.Warn("GlobalSettings", "打开数据目录失败:" + ex.Message);
        }
    }

    private async void OnRescanJava(object sender, RoutedEventArgs e)
    {
        JavaText.Text = Loc.T("正在扫描 Java…");
        await JavaEnv.RescanAsync();
        UpdateJavaText();
    }
}