using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.UI.Xaml.Controls;
using SL.Core.Account;
using SL.Core.Logging;
using SL.Core.Settings;
using SL.App.Dialogs;
using SL.App.Infrastructure;
using SL.App.Notify;

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
    public string KindText => Entry.Kind == "msa" ? "正版" : "离线";
    public string KindGlyph => Entry.Kind == "msa" ? "\uE8EA" : "\uE77B";
    public string Detail => Entry.Kind == "msa"
        ? Entry.Session is { } s
            ? $"令牌有效至 {s.MinecraftExpiresAt:MM-dd HH:mm}"
            : "本机缺少完整会话,请重新登录"
        : "离线账户 · 无需正版登录";

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
        LoginDeviceCodeCommand = new RelayCommand(async () => await LoginAsync(browser: false), () => !Busy);
        LoginBrowserCommand = new RelayCommand(async () => await LoginAsync(browser: true), () => !Busy);
        AddOfflineCommand = new RelayCommand(async () => await AddOfflineAsync(), () => !Busy);
        Refresh();
    }

    public ObservableCollection<AccountRowVM> Accounts { get; } = [];

    public RelayCommand LoginDeviceCodeCommand { get; }
    public RelayCommand LoginBrowserCommand { get; }
    public RelayCommand AddOfflineCommand { get; }

    private bool _busy;
    public bool Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                LoginDeviceCodeCommand.NotifyCanExecuteChanged();
                LoginBrowserCommand.NotifyCanExecuteChanged();
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
        AppServices.Notify.Publish(NotifyKind.Success, $"已切换到账户 {row.Name}");
        Refresh();
    }

    internal async Task DeleteAsync(AccountRowVM row)
    {
        if (!await AppServices.Dialogs.ConfirmAsync("删除账户",
                $"将删除「{row.Name}」({row.KindText});正版账户的会话令牌也会一并清除,该操作不可撤销。",
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
        AppServices.Notify.Publish(NotifyKind.Info, $"已删除账户 {row.Name}");
        Refresh();
    }

    private async Task LoginAsync(bool browser)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            ContentDialog dialog = browser ? new BrowserLoginDialog() : new DeviceCodeLoginDialog();
            await AppServices.Dialogs.ShowCustomAsync(dialog).ConfigureAwait(true);
        }
        finally
        {
            Busy = false;
            Refresh();
        }
    }

    private async Task AddOfflineAsync()
    {
        if (Busy)
            return;
        var name = await AppServices.Dialogs.PromptAsync("添加离线账户",
            "输入游戏内玩家名(1–16 个字符,仅限字母、数字、下划线)。").ConfigureAwait(true);
        if (name is null)
            return;
        if (!IsValidName(name))
        {
            await AppServices.Dialogs.AlertAsync("名字不合法",
                "玩家名需 1–16 个字符,仅限字母、数字、下划线。", MsgMark.Warning).ConfigureAwait(true);
            return;
        }
        var entry = AccountStore.AddOffline(name);
        SyncSettings(entry);
        AppLog.Info("Account", $"添加离线账户:{entry.Name}");
        AppServices.Notify.Publish(NotifyKind.Success, $"已添加离线账户 {entry.Name}");
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
            Detail = "尚未选择账户 · 启动时将使用离线名";
            return;
        }
        ActiveGlyph = active.Kind == "msa" ? "\uE8EA" : "\uE77B";
        Summary = active.Name;
        Detail = active.Kind == "msa"
            ? active.Session is { } s
                ? $"正版 · 令牌有效至 {s.MinecraftExpiresAt:MM-dd HH:mm}"
                : "正版 · 本机缺少完整会话,请重新登录"
            : "离线模式 · 无需正版账户";
    }
}
