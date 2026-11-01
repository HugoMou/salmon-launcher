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
using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SL.App.Dialogs;
using SL.App.Download;
using SL.App.Infrastructure;
using SL.App.Notify;
using SL.App.Localization;
using SL.Core.Boot;
using SL.Core.Launch;

namespace SL.App.Pages;

/// <summary>加载器条目。</summary>
public sealed class LoaderRow
{
    public LoaderRow(string name, string tag, string detail, string glyph, bool available, Action choose)
    {
        Name = name;
        Tag = tag;
        Detail = detail;
        Glyph = glyph;
        Available = available;
        ChooseCommand = new RelayCommand(choose);
    }

    public string Name { get; }
    public string Tag { get; }
    public string Detail { get; }
    public string Glyph { get; }
    public bool Available { get; }
    public RelayCommand ChooseCommand { get; }
}

/// <summary>加载器选择页:原版直接进入安装概览;Fabric / Quilt 可一键生成加载器实例。</summary>
public sealed partial class LoaderListPage : Page
{
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private bool _busy;

    public LoaderListPage()
    {
        InitializeComponent();
        LoaderList.ItemsSource = BuildRows();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        UpdateSummary();
    }

    private void UpdateSummary()
        => SummaryText.Text = InstallWizard.HasVersion
            ? string.Format(Loc.T("Minecraft {0} · 选择要一起安装的加载器"), InstallWizard.VersionId)
            : Loc.T("尚未选择游戏版本 · 请先回到「下载 → 游戏版本」选择一个版本");

    private IReadOnlyList<LoaderRow> BuildRows() =>
    [
        new(Loc.T("原版(纯净)"), Loc.T("推荐"), Loc.T("只安装官方核心、库与资源;可随时再加模组。"), "\uE7FC", true,
            () => ChooseVanilla()),
        new(Loc.T("Fabric"), Loc.T("模组"), Loc.T("轻量模组加载器:自动获取官方 profile 并生成可启动实例。"), "\uE7B8", true,
            () => InstallLoaderAsync("Fabric")),
        new(Loc.T("Quilt"), Loc.T("模组"), Loc.T("Fabric 衍生加载器:同样走官方 profile 一键生成实例。"), "\uE7B8", true,
            () => InstallLoaderAsync("Quilt")),
        new(Loc.T("Forge"), Loc.T("模组"), Loc.T("老牌模组加载器:自动下载并运行官方安装器,完成后直接可启动。"), "\uE7B8", true,
            () => InstallLoaderAsync("Forge")),
        new(Loc.T("NeoForge"), Loc.T("模组"), Loc.T("1.20.2 起的 Forge 分支:自动运行官方安装器并生成实例。"), "\uE7B8", true,
            () => InstallLoaderAsync("NeoForge")),
    ];

    private void ChooseVanilla()
    {
        if (!InstallWizard.HasVersion)
        {
            AppServices.Notify.Publish(NotifyKind.Warning, Loc.T("请先在「游戏版本」里选择一个版本"));
            return;
        }
        InstallWizard.ChooseLoader("原版");
        AppServices.Nav.Navigate("dl.install");
    }

    /// <summary>Fabric / Quilt / Forge / NeoForge:生成实例元数据并提交加载器库下载。</summary>
    private async void InstallLoaderAsync(string loader)
    {
        if (_busy)
            return;

        if (!InstallWizard.HasVersion)
        {
            AppServices.Notify.Publish(NotifyKind.Warning, Loc.T("请先在「游戏版本」里选择一个版本"));
            return;
        }

        var game = InstallWizard.VersionId;
        var vanillaJson = Path.Combine(Runtime.InstancesDir, game, "meta", game + ".json");
        var vanillaJar = Path.Combine(Runtime.InstancesDir, game, "versions", game + ".jar");
        if (!File.Exists(vanillaJson) || !File.Exists(vanillaJar))
        {
            await AppServices.Dialogs.AlertAsync(
                string.Format(Loc.T("请先安装原版 {0}"), game),
                string.Format(Loc.T("加载器需要以原版 {0} 为基础。请先在「安装概览」完成原版安装,再回到本页添加 {1}。"), game, loader),
                MsgMark.Info).ConfigureAwait(true);
            return;
        }

        _busy = true;
        var started = DateTimeOffset.Now;
        SummaryText.Text = string.Format(Loc.T("正在准备 {0}…"), loader);
        try
        {
            var plan = await LoaderInstall.BuildPlanAsync(loader, game, null, vanillaJson, vanillaJar,
                    CancellationToken.None,
                    step => _ui.TryEnqueue(() => SummaryText.Text = step))
                .ConfigureAwait(true);
            var count = InstallWizard.SubmitLoaderFiles(plan.Files);

            var seconds = (int)(DateTimeOffset.Now - started).TotalSeconds;
            AppServices.Notify.Publish(NotifyKind.Success,
                string.Format(Loc.T("{0} 实例 {1} 已创建,开始下载 {2} 个库文件"), loader, plan.InstanceId, count));
            SummaryText.Text =
                string.Format(Loc.T("{0} 实例已创建({1}s)· 下载完成后可在「实例 → 概览」启动"), loader, seconds);
            AppServices.Nav.Navigate("launch.tasks");
        }
        catch (Exception ex)
        {
            SL.Core.Logging.AppLog.Error("LoaderInstall", ex);
            SummaryText.Text = string.Format(Loc.T("{0} 安装失败:{1}"), loader, ex.Message);
            AppServices.Notify.Publish(NotifyKind.Error, string.Format(Loc.T("{0} 安装失败:"), loader) + ex.Message);
            UpdateSummary();
        }
        finally
        {
            _busy = false;
        }
    }
}
