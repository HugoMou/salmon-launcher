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
using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SL.Core.Account;
using SL.Core.Instances;
using SL.Core.Launch;
using SL.Core.Logging;
using SL.App.Dialogs;
using SL.App.Localization;
using SL.App.Infrastructure;
using SL.App.Notify;

namespace SL.App.Pages;

/// <summary>A row in the instance list.</summary>
public sealed class InstanceRow
{
    public InstanceRow(InstanceInfo info)
    {
        Id = info.InstanceId;
        Detail = string.Format(Loc.T("安装于 {0} · {1} MB"), info.InstalledAt.ToString("yyyy-MM-dd HH:mm"), info.SizeMb);
    }

    public string Id { get; }
    public string Detail { get; }

    public override string ToString() => Id;
}

/// <summary>Home view model: instance selection, account identity, one-click launch, and shortcuts.</summary>
public sealed class HomeVM : VmBase
{
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();

    public HomeVM()
    {
        LaunchCommand = new RelayCommand(async () => await LaunchAsync(), () => !Busy);
        OpenDownloadCommand = new RelayCommand(() => AppServices.Nav.Navigate("dl.game"));
        OpenMultiplayerCommand = new RelayCommand(() => AppServices.Nav.Navigate("tools.online"));
        OpenDiagCommand = new RelayCommand(() => AppServices.Nav.Navigate("tools.diag"));
        OpenLogsCommand = new RelayCommand(() => AppServices.Nav.Navigate("tools.log"));
        OpenAccountCommand = new RelayCommand(() => AppServices.Nav.Navigate("st.launch"));
        // After creating / signing in to an account in the welcome page, the home card must update immediately (the user has not left home yet, so re-entry refresh will not run)
        AppServices.Settings.Changed += _ => RefreshAccount();
        Refresh();
    }

    public ObservableCollection<InstanceRow> Instances { get; } = [];

    public RelayCommand LaunchCommand { get; }
    public RelayCommand OpenDownloadCommand { get; }
    public RelayCommand OpenMultiplayerCommand { get; }
    public RelayCommand OpenDiagCommand { get; }
    public RelayCommand OpenLogsCommand { get; }
    public RelayCommand OpenAccountCommand { get; }

    private InstanceRow? _selectedInstance;
    public InstanceRow? SelectedInstance
    {
        get => _selectedInstance;
        set => Set(ref _selectedInstance, value);
    }

    private string? _lastSelectedId;

    private bool _busy;
    public bool Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                LaunchCommand.NotifyCanExecuteChanged();
                On(nameof(ProgressVisibility));
                On(nameof(LaunchHint));
            }
        }
    }

    public Visibility ProgressVisibility => Busy ? Visibility.Visible : Visibility.Collapsed;

    private string _statusText = Loc.T("就绪");
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }

    public string LaunchHint => Busy ? Loc.T("正在启动,请稍候…") : Loc.T("点击开始游戏 · 使用所选实例与当前账户");

    private string _accountGlyph = "\uE77B";
    public string AccountGlyph { get => _accountGlyph; private set => Set(ref _accountGlyph, value); }

    private string _accountName = "";
    public string AccountName { get => _accountName; private set => Set(ref _accountName, value); }

    private string _accountDetail = "";
    public string AccountDetail { get => _accountDetail; private set => Set(ref _accountDetail, value); }

    private string _instanceSummary = "";
    public string InstanceSummary { get => _instanceSummary; private set => Set(ref _instanceSummary, value); }

    public void Refresh()
    {
        var fresh = InstanceRegistry.List().Select(i => new InstanceRow(i)).ToList();
        Instances.Clear();
        foreach (var row in fresh)
            Instances.Add(row);

        // Force one notification after rebuilding the collection to avoid losing selection when the ComboBox binds SelectedItem first
        SelectedInstance = null;
        SelectedInstance = fresh.FirstOrDefault(r => r.Id == _lastSelectedId) ?? fresh.FirstOrDefault();
        _lastSelectedId = SelectedInstance?.Id;

        InstanceSummary = fresh.Count == 0
            ? Loc.T("尚无实例 · 可前往「下载 → 游戏版本」安装")
            : string.Format(Loc.T("共 {0} 个实例 · 就绪"), fresh.Count);

        RefreshAccount();
    }

    private void RefreshAccount()
    {
        var repo = AccountStore.Load();
        var active = repo.Accounts.FirstOrDefault(a => a.Id == repo.ActiveId);
        if (active is null)
        {
            AccountGlyph = "\uE77B";
            AccountName = AppServices.Settings.Current.AccountName;
            AccountDetail = Loc.T("离线名 · 前往「外观与设置」管理账户");
        }
        else
        {
            AccountGlyph = active.Kind == "msa" ? "\uE8EA" : "\uE77B";
            AccountName = active.Name;
            AccountDetail = (active.Kind == "msa" ? Loc.T("正版账户") : Loc.T("离线账户")) + " · " + Loc.T("点击前往账户管理");
        }
    }

    private async Task LaunchAsync()
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            var repo = AccountStore.Load();
            var active = repo.Accounts.FirstOrDefault(a => a.Id == repo.ActiveId);
            var accountName = active?.Name ?? AppServices.Settings.Current.AccountName;
            LaunchPipeline.LaunchIdentity? identity = null;

            if (active?.Kind == "msa")
            {
                if (active.Session is not { } session)
                {
                    AppServices.Notify.Publish(NotifyKind.Warning, string.Format(Loc.T("账户 {0} 缺少完整会话,请到「外观与设置」重新登录。"), active.Name));
                    return;
                }
                StatusText = Loc.T("正在准备正版会话…");
                session = await MsAuth.EnsureFreshAsync(session,
                    step => _ui.TryEnqueue(() => StatusText = step)).ConfigureAwait(true);
                AccountStore.UpsertMsa(session);
                identity = new LaunchPipeline.LaunchIdentity(session.Name, session.Uuid, session.MinecraftAccessToken, "msa");
                accountName = session.Name;
            }

            var instanceId = SelectedInstance?.Id ?? LaunchPipeline.LatestInstanceId();
            if (string.IsNullOrEmpty(instanceId))
            {
                await AppServices.Dialogs.AlertAsync(Loc.T("尚未安装任何版本"),
                    Loc.T("请先到「下载 → 游戏版本」安装一个实例,再回到本页启动。"),
                    MsgMark.Info).ConfigureAwait(true);
                return;
            }

            var cfg = AppServices.Settings.Current;
            StatusText = string.Format(Loc.T("正在准备实例 {0}…"), instanceId);
            LaunchPipeline.Step += OnPipelineStep;
            LaunchResult result;
            try
            {
                result = await LaunchPipeline.StartAsync(instanceId, accountName, cfg.MemoryMb,
                    CancellationToken.None, identity, cfg.JvmExtraArgs, cfg.GameWidth, cfg.GameHeight)
                    .ConfigureAwait(true);
            }
            finally
            {
                LaunchPipeline.Step -= OnPipelineStep;
            }

            if (result.Success)
            {
                AppLog.Info("Launch", $"游戏已启动:{instanceId}(PID {result.ProcessId},{accountName})");
                AppServices.Notify.Publish(NotifyKind.Success, Loc.T("游戏已启动!"));
                StatusText = string.Format(Loc.T("游戏运行中(PID {0}) · 关闭游戏后可继续操作"), result.ProcessId);
            }
            else
            {
                AppLog.Info("Launch", "启动失败:" + result.Error);
                AppServices.Notify.Publish(NotifyKind.Error, Loc.T("启动失败:") + (result.Error ?? Loc.T("未知原因")));
                StatusText = Loc.T("启动失败:") + (result.Error ?? Loc.T("未知原因")) + Loc.T(" · 可到「工具 → 崩溃诊断」分析日志");
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Launch", ex);
            AppServices.Notify.Publish(NotifyKind.Error, Loc.T("启动失败:") + ex.Message);
            StatusText = Loc.T("启动失败:") + ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    private void OnPipelineStep(string step)
        => _ui.TryEnqueue(() => StatusText = step);
}
