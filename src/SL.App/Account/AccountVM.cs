using System.Windows.Input;
using SL.Core.Account;
using SL.Core.Logging;
using SL.Core.Settings;
using SL.App.Dialogs;
using SL.App.Infrastructure;
using SL.App.Notify;

namespace SL.App.Account;

/// <summary>账户区视图模型:离线名与微软正版(设备码流程)的状态展示与切换。</summary>
public sealed class AccountVM : VmBase
{
    private readonly ISettingsService _settings;

    public AccountVM()
    {
        _settings = AppServices.Settings;
        _settings.Changed += _ => RefreshFromConfig();
        LoginCommand = new RelayCommand(async () => await LoginDeviceCodeAsync(), () => !Busy);
        LogoutCommand = new RelayCommand(async () => await LogoutAsync(), () => !Busy && IsMsa);
        RefreshFromConfig();
    }

    public RelayCommand LoginCommand { get; }
    public RelayCommand LogoutCommand { get; }

    private bool _busy;
    public bool Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
            {
                LoginCommand.NotifyCanExecuteChanged();
                LogoutCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private bool _isMsa;
    public bool IsMsa
    {
        get => _isMsa;
        private set
        {
            if (Set(ref _isMsa, value))
                On(nameof(LogoutVisibility));
        }
    }

    public Microsoft.UI.Xaml.Visibility LogoutVisibility
        => IsMsa ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

    private string _summary = "";
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    private string _detail = "";
    public string Detail { get => _detail; private set => Set(ref _detail, value); }

    private async Task LoginDeviceCodeAsync()
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            await AppServices.Dialogs.ShowCustomAsync(new DeviceCodeLoginDialog()).ConfigureAwait(true);
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task LogoutAsync()
    {
        if (!await AppServices.Dialogs.ConfirmAsync("退出正版登录", "将删除本机保存的微软会话;下次联机需要重新登录。").ConfigureAwait(true))
            return;
        MsAuth.ClearSession();
        _settings.Update(c => c.AccountType = "offline");
        AppLog.Info("Account", "已退出正版登录");
        AppServices.Notify.Publish(NotifyKind.Info, "已退出正版登录");
    }

    private void RefreshFromConfig()
    {
        var cfg = _settings.Current;
        IsMsa = cfg.AccountType == "msa";
        if (IsMsa)
        {
            var s = MsAuth.LoadSession();
            Summary = s is { Name.Length: > 0 } ? s.Name : cfg.AccountName;
            Detail = s is null
                ? "正版 · 本机会话缺失,请重新登录"
                : $"正版 · 令牌有效至 {s.MinecraftExpiresAt:MM-dd HH:mm}";
        }
        else
        {
            Summary = cfg.AccountName;
            Detail = "离线模式 · 无需正版账户";
        }
    }
}
