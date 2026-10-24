using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SL.Core.Account;
using SL.Core.Instances;
using SL.Core.Launch;
using SL.Core.Logging;
using SL.App.Dialogs;
using SL.App.Instances;
using SL.App.Infrastructure;
using SL.App.Notify;

namespace SL.App.Pages;

/// <summary>实例列表行(概览页下拉用)。</summary>
public sealed class InstancePick
{
    public InstancePick(InstanceInfo info)
    {
        Info = info;
        Id = info.InstanceId;
    }

    public InstanceInfo Info { get; }
    public string Id { get; }

    public override string ToString() => Id;
}

/// <summary>实例概览页:实例信息、计数与快捷操作。</summary>
public sealed partial class InstanceOverviewPage : Page
{
    private readonly ObservableCollection<InstancePick> _picks = [];
    private InstanceInfo? _current;
    private bool _syncing;

    public InstanceOverviewPage()
    {
        InitializeComponent();
        InstanceBox.ItemsSource = _picks;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Reload(selectCurrent: true);
    }

    private void Reload(bool selectCurrent)
    {
        _syncing = true;
        var all = InstanceContext.Refresh();
        _picks.Clear();
        foreach (var info in all)
            _picks.Add(new InstancePick(info));

        _current = all.FirstOrDefault(i => i.InstanceId == InstanceContext.CurrentId);
        InstanceBox.SelectedItem = _picks.FirstOrDefault(p => p.Id == InstanceContext.CurrentId);
        _syncing = false;

        if (_current is null)
        {
            StatusText.Text = "尚未安装实例 · 前往「下载 → 游戏版本」安装一个";
            InstanceId.Text = "无实例";
            InstalledText.Text = "";
            ModCount.Text = "—";
            SaveCount.Text = "—";
            SizeText.Text = "—";
            return;
        }

        InstanceId.Text = _current.InstanceId;
        InstalledText.Text = $"安装于 {_current.InstalledAt:yyyy-MM-dd HH:mm} · 目录 {InstanceRegistry.InstanceDirOf(_current.InstanceId)}";
        var mods = InstanceRegistry.ListMods(_current.InstanceId);
        ModCount.Text = $"{mods.Count(m => m.Enabled)} / {mods.Count}";
        SaveCount.Text = InstanceRegistry.ListSaves(_current.InstanceId).Count.ToString();
        SizeText.Text = $"{_current.SizeMb} MB";
        StatusText.Text = $"共 {all.Count} 个实例 · 当前查看 {_current.InstanceId}";
    }

    private void OnInstanceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || InstanceBox.SelectedItem is not InstancePick pick)
            return;
        InstanceContext.Select(pick.Id);
        Reload(selectCurrent: true);
    }

    private async void OnLaunch(object sender, RoutedEventArgs e)
    {
        if (_current is null)
        {
            await AppServices.Dialogs.AlertAsync("尚未安装实例", "请先到「下载 → 游戏版本」安装一个实例。",
                MsgMark.Info).ConfigureAwait(true);
            return;
        }

        var cfg = AppServices.Settings.Current;
        var repo = AccountStore.Load();
        var active = repo.Accounts.FirstOrDefault(a => a.Id == repo.ActiveId);
        var accountName = active?.Name ?? cfg.AccountName;
        LaunchPipeline.LaunchIdentity? identity = null;

        if (active?.Kind == "msa" && active.Session is { } session)
        {
            try
            {
                session = await MsAuth.EnsureFreshAsync(session, _ => { }).ConfigureAwait(true);
                AccountStore.UpsertMsa(session);
                identity = new LaunchPipeline.LaunchIdentity(session.Name, session.Uuid,
                    session.MinecraftAccessToken, "msa");
                accountName = session.Name;
            }
            catch (Exception ex)
            {
                AppLog.Error("InstanceOverview", ex);
                AppServices.Notify.Publish(NotifyKind.Warning, "正版会话续签失败,将回退为离线启动");
            }
        }

        StatusText.Text = "正在启动…";
        try
        {
            var result = await LaunchPipeline.StartAsync(_current.InstanceId, accountName, cfg.MemoryMb,
                CancellationToken.None, identity, cfg.JvmExtraArgs, cfg.GameWidth, cfg.GameHeight)
                .ConfigureAwait(true);
            if (result.Success)
                AppServices.Notify.Publish(NotifyKind.Success, $"已启动 {_current.InstanceId}(PID {result.ProcessId})");
            else
                AppServices.Notify.Publish(NotifyKind.Error, "启动失败:" + (result.Error ?? "未知原因"));
            StatusText.Text = result.Success ? "游戏运行中" : "启动失败:" + (result.Error ?? "未知原因");
        }
        catch (Exception ex)
        {
            AppLog.Error("InstanceOverview", ex);
            AppServices.Notify.Publish(NotifyKind.Error, "启动失败:" + ex.Message);
        }
    }

    private void OnOpenDir(object sender, RoutedEventArgs e)
    {
        if (_current is null)
            return;
        OpenPath(InstanceRegistry.InstanceDirOf(_current.InstanceId));
    }

    private async void OnBackupSaves(object sender, RoutedEventArgs e)
    {
        if (_current is null)
            return;
        try
        {
            var path = InstanceRegistry.BackupSaves(_current.InstanceId);
            if (path is null)
            {
                await AppServices.Dialogs.AlertAsync("没有可备份的存档",
                    "该实例的 saves 目录为空;进入游戏创建世界后再来备份。", MsgMark.Info).ConfigureAwait(true);
                return;
            }
            AppServices.Notify.Publish(NotifyKind.Success, "存档已备份:" + System.IO.Path.GetFileName(path));
            StatusText.Text = "已备份到 " + path;
        }
        catch (Exception ex)
        {
            AppLog.Error("InstanceOverview", ex);
            AppServices.Notify.Publish(NotifyKind.Error, "备份失败:" + ex.Message);
        }
    }

    private void OnOpenMods(object sender, RoutedEventArgs e) => AppServices.Nav.Navigate("inst.mods");

    private void OnOpenLogs(object sender, RoutedEventArgs e) => AppServices.Nav.Navigate("inst.logs");

    private void OnOpenCfg(object sender, RoutedEventArgs e) => AppServices.Nav.Navigate("inst.cfg");

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("InstanceOverview", "打开目录失败:" + ex.Message);
        }
    }
}