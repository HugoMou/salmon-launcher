using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SL.Core.Instances;
using SL.Core.Logging;
using SL.App.Dialogs;
using SL.App.Instances;
using SL.App.Infrastructure;
using SL.App.Notify;

namespace SL.App.Pages;

/// <summary>实例设置页:启动参数(全局生效)与实例目录维护。</summary>
public sealed partial class InstanceConfigPage : Page
{
    private readonly ObservableCollection<InstancePick> _picks = [];
    private bool _syncing;
    private bool _loading;

    public InstanceConfigPage()
    {
        InitializeComponent();
        InstanceBox.ItemsSource = _picks;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _syncing = true;
        var all = InstanceContext.Refresh();
        _picks.Clear();
        foreach (var info in all)
            _picks.Add(new InstancePick(info));
        InstanceBox.SelectedItem = _picks.FirstOrDefault(p => p.Id == InstanceContext.CurrentId);
        _syncing = false;
        LoadValues();
    }

    private void LoadValues()
    {
        _loading = true;
        var cfg = AppServices.Settings.Current;
        MemoryBox.Value = cfg.MemoryMb;
        WidthBox.Value = cfg.GameWidth;
        HeightBox.Value = cfg.GameHeight;
        JvmBox.Text = cfg.JvmExtraArgs;
        _loading = false;

        if (InstanceContext.CurrentId.Length == 0)
        {
            StatusText.Text = "尚未安装实例 · 请先安装一个版本";
            PathText.Text = "—";
            return;
        }

        StatusText.Text = $"当前实例 {InstanceContext.CurrentId}";
        PathText.Text = InstanceRegistry.InstanceDirOf(InstanceContext.CurrentId);
    }

    private void OnInstanceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || InstanceBox.SelectedItem is not InstancePick pick)
            return;
        InstanceContext.Select(pick.Id);
        LoadValues();
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

    private void OnOpenDir(object sender, RoutedEventArgs e)
    {
        if (InstanceContext.CurrentId.Length == 0)
            return;
        try
        {
            var dir = InstanceRegistry.InstanceDirOf(InstanceContext.CurrentId);
            System.IO.Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("InstanceConfig", "打开目录失败:" + ex.Message);
        }
    }

    private async void OnDeleteInstance(object sender, RoutedEventArgs e)
    {
        if (InstanceContext.CurrentId.Length == 0)
            return;
        var id = InstanceContext.CurrentId;

        if (!await AppServices.Dialogs.ConfirmAsync("删除实例",
                $"将删除实例「{id}」的整个目录(含模组、存档与日志),该操作不可撤销。\n"
                + "共享的库与资源不会被删除。", MsgMark.Warning, okLabel: "删除").ConfigureAwait(true))
            return;

        try
        {
            var dir = InstanceRegistry.InstanceDirOf(id);
            if (System.IO.Directory.Exists(dir))
                System.IO.Directory.Delete(dir, recursive: true);
            AppLog.Info("InstanceConfig", $"已删除实例 {id}");
            AppServices.Notify.Publish(NotifyKind.Info, $"已删除实例 {id}");
            InstanceContext.Select("");
            InstanceContext.Refresh();
            LoadValues();
        }
        catch (Exception ex)
        {
            AppLog.Error("InstanceConfig", ex);
            AppServices.Notify.Publish(NotifyKind.Error, "删除失败:" + ex.Message);
        }
    }
}