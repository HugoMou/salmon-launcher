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
using SL.Core.Boot;
using SL.Core.Logging;
using SL.Core.Net;
using SL.App.Download;
using SL.App.Infrastructure;
using SL.App.Notify;

namespace SL.App.Pages;

/// <summary>安装概览页:读取版本元数据、展示计划并提交下载任务。</summary>
public sealed partial class InstallPage : Page
{
    private MojangMeta.VersionDetail? _detail;
    private bool _busy;

    public InstallPage()
    {
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        TitleText.Text = InstallWizard.HasVersion
            ? $"安装 {InstallWizard.Loader} → Minecraft {InstallWizard.VersionId}"
            : "安装概览";
        PathText.Text = InstallWizard.HasVersion
            ? System.IO.Path.Combine(Runtime.InstancesDir, InstallWizard.VersionId)
            : Runtime.InstancesDir;
        await LoadPlanAsync();
    }

    private async Task LoadPlanAsync()
    {
        InstallButton.IsEnabled = false;
        PlanList.ItemsSource = new[] { "正在读取版本元数据…" };

        if (!InstallWizard.HasVersion)
        {
            StageText.Text = "尚未选择版本";
            PlanList.ItemsSource = new[] { "请先到「下载 → 游戏版本」选择一个版本。" };
            return;
        }

        if (InstallWizard.VersionUrl.Length == 0)
        {
            StageText.Text = "缺少版本链接";
            PlanList.ItemsSource = new[] { "当前没有可用的版本元数据链接,请返回版本页重新选择后重试。" };
            return;
        }

        try
        {
            StageText.Text = "正在读取版本元数据…";
            _detail = await MojangMeta.GetVersionAsync(InstallWizard.VersionUrl, CancellationToken.None)
                .ConfigureAwait(true);
            InstallWizard.Detail = _detail;
        }
        catch (Exception ex)
        {
            AppLog.Error("Install", ex);
            StageText.Text = "元数据读取失败";
            PlanList.ItemsSource = new[] { "元数据读取失败:" + ex.Message, "恢复网络后重新进入本页会自动重试。" };
            return;
        }

        if (_detail?.Client is null)
        {
            StageText.Text = "版本 JSON 不完整";
            PlanList.ItemsSource = new[] { "版本 JSON 缺少 client 下载信息,无法生成安装计划。" };
            return;
        }

        var natives = _detail.Libraries.Count(l => l.IsNative);
        PlanList.ItemsSource = new[]
        {
            $"版本元数据 {_detail.Id}.json(写入 instances/{_detail.Id}/meta)",
            $"游戏核心 {_detail.Id}.jar(约 {Fmt(_detail.Client.Size)})",
            $"库文件 {_detail.Libraries.Count} 个(约 {Fmt(_detail.LibraryBytes)},含 {natives} 个 Windows natives)",
            $"资源索引 assets/indexes/{_detail.AssetId}.json(约 {Fmt(_detail.AssetIndex?.Size ?? -1)})",
            "资源对象与 Java 运行时会在我首次启动该实例时按差量自动补齐",
        };
        StageText.Text = $"共 {2 + _detail.Libraries.Count + (_detail.AssetIndex is null ? 0 : 1)} 个下载任务 · 逐文件 SHA-1 校验";
        InstallButton.IsEnabled = true;
    }

    private static string Fmt(long bytes)
        => bytes < 0 ? "未知"
            : bytes >= 1 << 20 ? $"{bytes / (1024.0 * 1024.0):F1} MB"
            : $"{bytes / 1024.0:F0} KB";

    private void OnBack(object sender, RoutedEventArgs e)
        => AppServices.Nav.Navigate("dl.game");

    private void OnInstall(object sender, RoutedEventArgs e)
    {
        if (_busy || _detail is null)
            return;
        _busy = true;
        InstallButton.IsEnabled = false;
        try
        {
            var count = InstallWizard.SubmitInstall(_detail);
            AppLog.Info("Install", $"已提交 {_detail.Id} 安装任务 {count} 个");
            AppServices.Notify.Publish(NotifyKind.Success, $"已提交 {count} 个安装任务,可在任务中心查看进度");
            AppServices.Nav.Navigate("launch.tasks");
        }
        catch (Exception ex)
        {
            AppLog.Error("Install", ex);
            AppServices.Notify.Publish(NotifyKind.Error, "提交安装失败:" + ex.Message);
            InstallButton.IsEnabled = true;
        }
        finally
        {
            _busy = false;
        }
    }
}