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
using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.UI.Xaml.Controls;
using SL.Core.Account;
using SL.Core.Logging;
using SL.Core.Settings;
using SL.App.Dialogs;
using SL.App.Infrastructure;
using SL.App.Notify;
using SL.App.Localization;

namespace SL.App.Account;

/// <summary>账户列表中的一行:正版或离线账户,支持切换为活动与删除。</summary>
public sealed class AccountRowVM : VmBase
{
    private readonly AccountVM _owner;

    public AccountRowVM(AccountVM owner, AccountEntry entry)
    {
        _owner = owner;
        Entry = entry;
        UseCommand = new RelayCommand(() => _owner.Use(this));
        DeleteCommand = new RelayCommand(async () => await _owner.DeleteAsync(this));
    }

    public AccountEntry Entry { get; }
    public string Id => Entry.Id;
    public string Name => Entry.Name;
    public string KindText => Entry.Kind == "msa" ? Loc.T("正版") : Loc.T("离线");
    public string KindGlyph => Entry.Kind == "msa" ? "\uE8EA" : "\uE77B";
    public string Detail => Entry.Kind == "msa"
        ? Entry.Session is { } s
            ? string.Format(Loc.T("令牌有效至 {0}"), s.MinecraftExpiresAt.ToString("MM-dd HH:mm"))
            : Loc.T("本机缺少完整会话,请重新登录")
        : Loc.T("离线账户 · 无需正版登录");

    private bool _active;
    public bool Active
    {
        get => _active;
        internal set
        {
            if (Set(ref _active, value))
            {
                On(nameof(ActiveVisibility));
                On(nameof(Inactive));
            }
        }
    }

    public Microsoft.UI.Xaml.Visibility ActiveVisibility
        => Active ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    public bool Inactive => !Active;

    public ICommand UseCommand { get; }
    public ICommand DeleteCommand { get; }
}

/// <summary>账户区视图模型:多账户仓库(正版 + 离线混合)的展示、切换、删除与两种正版登录入口。</summary>
public sealed class AccountVM : VmBase
{
    private readonly ISettingsService _settings;

    public AccountVM()
    {
        _settings = AppServices.Settings;
        _settings.Changed += _ => RefreshActiveFromStore();
        LoginPremiumCommand = new RelayCommand(async () => await LoginChoiceAsync(), () => !Busy);
        AddOfflineCommand = new RelayCommand(async () => await AddOfflineAsync(), () => !Busy);
        Refresh();
    }

    public ObservableCollection<AccountRowVM> Accounts { get; } = [];

    public RelayCommand LoginPremiumCommand { get; }
    public RelayCommand AddOfflineCommand { get; }

    private bool _busy;
    public bool Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                LoginPremiumCommand.NotifyCanExecuteChanged();
                AddOfflineCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private string _summary = "";
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    private string _detail = "";
    public string Detail { get => _detail; private set => Set(ref _detail, value); }

    private string _activeGlyph = "\uE77B";
    public string ActiveGlyph { get => _activeGlyph; private set => Set(ref _activeGlyph, value); }

    internal void Use(AccountRowVM row)
    {
        if (row.Active)
            return;
        AccountStore.SetActive(row.Id);
        SyncSettings(row.Entry);
        AppLog.Info("Account", $"切换账户:{row.Name}({row.KindText})");
        AppServices.Notify.Publish(NotifyKind.Success, string.Format(Loc.T("已切换到账户 {0}"), row.Name));
        Refresh();
    }

    internal async Task DeleteAsync(AccountRowVM row)
    {
        if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("删除账户"),
                string.Format(Loc.T("将删除「{0}」({1});正版账户的会话令牌也会一并清除,该操作不可撤销。"), row.Name, row.KindText),
                MsgMark.Warning).ConfigureAwait(true))
            return;
        AccountStore.Remove(row.Id);
        var repo = AccountStore.Load();
        var active = repo.Accounts.FirstOrDefault(a => a.Id == repo.ActiveId) ?? repo.Accounts.FirstOrDefault();
        if (active is not null)
        {
            AccountStore.SetActive(active.Id);
            SyncSettings(active);
        }
        else
        {
            _settings.Update(c => c.AccountType = "offline");
        }
        AppLog.Info("Account", $"已删除账户:{row.Name}");
        AppServices.Notify.Publish(NotifyKind.Info, string.Format(Loc.T("已删除账户 {0}"), row.Name));
        Refresh();
    }

    /// <summary>正版登录:先选择登录方式(内置浏览器 / 设备码 / 复制授权链接)。</summary>
    private async Task LoginChoiceAsync()
    {
        if (Busy)
            return;

        var choice = await AppServices.Dialogs.ChooseAsync(
            Loc.T("正版授权登录"),
            Loc.T("选择一种登录方式;令牌只保存在本机。"),
            [
                Loc.T("浏览器授权(在启动器内置窗口直接登录)"),
                Loc.T("设备码登录(到 microsoft.com/link 输入一次性代码)"),
                Loc.T("系统浏览器登录(自动打开浏览器,完成后把回跳链接粘贴回来)"),
            ]).ConfigureAwait(true);
        if (choice is null)
            return;

        Busy = true;
        try
        {
            switch (choice.Value)
            {
                case 0:
                    await AppServices.Dialogs.ShowCustomAsync(new BrowserLoginDialog()).ConfigureAwait(true);
                    break;
                case 1:
                    await AppServices.Dialogs.ShowCustomAsync(new DeviceCodeLoginDialog()).ConfigureAwait(true);
                    break;
                default:
                    await LoginByUrlAsync().ConfigureAwait(true);
                    break;
            }
        }
        finally
        {
            Busy = false;
            Refresh();
        }
    }

    /// <summary>复制授权链接:链接进剪贴板并直接唤起系统浏览器,登录完成后把回跳链接粘回来换取会话。</summary>
    private async Task LoginByUrlAsync()
    {
        var req = MsAuth.BeginInteractive();
        CopyToClipboard(req.Url);

        var dialog = new ExternalLoginDialog(req.Url);
        var result = await AppServices.Dialogs.ShowCustomAsync(dialog).ConfigureAwait(true);
        if (result != ContentDialogResult.Primary || dialog.CallbackUrl is not { } callback)
            return;

        try
        {
            AppServices.Notify.Publish(NotifyKind.Info, Loc.T("正在完成微软登录…"));
            var code = MsAuth.ExtractAuthorizationCode(callback);
            var session = await MsAuth.CompleteInteractiveAsync(code, req.Verifier).ConfigureAwait(true);
            var entry = AccountStore.UpsertMsa(session);
            _settings.Update(c =>
            {
                c.AccountType = "msa";
                c.AccountName = session.Name;
                c.ActiveAccountId = entry.Id;
            });
            AppLog.Info("Account", $"正版登录成功(复制链接):{session.Name}");
            AppServices.Notify.Publish(NotifyKind.Success, string.Format(Loc.T("正版登录成功:{0}"), session.Name));
        }
        catch (Exception ex)
        {
            AppLog.Error("Account", ex);
            await AppServices.Dialogs.AlertAsync(Loc.T("登录未完成"), ex.Message, MsgMark.Warning).ConfigureAwait(true);
        }
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage
            {
                RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy,
            };
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            Windows.ApplicationModel.DataTransfer.Clipboard.Flush();
        }
        catch (Exception ex)
        {
            AppLog.Warn("Account", "复制授权链接失败:" + ex.Message);
        }
    }

    private async Task AddOfflineAsync()
    {
        if (Busy)
            return;
        var name = await AppServices.Dialogs.PromptAsync(Loc.T("添加离线账户"),
            Loc.T("输入游戏内玩家名(1–16 个字符,仅限字母、数字、下划线)。")).ConfigureAwait(true);
        if (name is null)
            return;
        if (!IsValidName(name))
        {
            await AppServices.Dialogs.AlertAsync(Loc.T("名字不合法"),
                Loc.T("玩家名需 1–16 个字符,仅限字母、数字、下划线。"), MsgMark.Warning).ConfigureAwait(true);
            return;
        }
        var entry = AccountStore.AddOffline(name);
        SyncSettings(entry);
        AppLog.Info("Account", $"添加离线账户:{entry.Name}");
        AppServices.Notify.Publish(NotifyKind.Success, string.Format(Loc.T("已添加离线账户 {0}"), entry.Name));
        Refresh();
    }

    private static bool IsValidName(string name)
        => name.Length is >= 1 and <= 16 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');

    private void SyncSettings(AccountEntry entry)
    {
        _settings.Update(c =>
        {
            c.ActiveAccountId = entry.Id;
            c.AccountType = entry.Kind;
            c.AccountName = entry.Name;
        });
    }

    private void Refresh()
    {
        var repo = AccountStore.Load();
        Accounts.Clear();
        foreach (var a in repo.Accounts)
            Accounts.Add(new AccountRowVM(this, a) { Active = a.Id == repo.ActiveId });
        RefreshActiveFromStore();
    }

    private void RefreshActiveFromStore()
    {
        var repo = AccountStore.Load();
        var active = repo.Accounts.FirstOrDefault(a => a.Id == repo.ActiveId);
        foreach (var row in Accounts)
            row.Active = row.Id == repo.ActiveId;
        if (active is null)
        {
            ActiveGlyph = "\uE77B";
            Summary = _settings.Current.AccountName;
            Detail = Loc.T("尚未选择账户 · 启动时将使用离线名");
            return;
        }
        ActiveGlyph = active.Kind == "msa" ? "\uE8EA" : "\uE77B";
        Summary = active.Name;
        Detail = active.Kind == "msa"
            ? active.Session is { } s
                ? string.Format(Loc.T("正版 · 令牌有效至 {0}"), s.MinecraftExpiresAt.ToString("MM-dd HH:mm"))
                : Loc.T("正版 · 本机缺少完整会话,请重新登录")
            : Loc.T("离线模式 · 无需正版账户");
    }
}
