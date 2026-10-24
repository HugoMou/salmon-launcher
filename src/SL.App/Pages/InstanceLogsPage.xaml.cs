using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SL.Core.Logging;
using SL.Core.Tools;
using SL.App.Instances;
using SL.App.Infrastructure;

namespace SL.App.Pages;

/// <summary>实例日志页:读取 latest.log 尾部并按关键词过滤。</summary>
public sealed partial class InstanceLogsPage : Page
{
    private readonly ObservableCollection<InstancePick> _picks = [];
    private bool _syncing;

    public InstanceLogsPage()
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
        Load();
    }

    private void OnInstanceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || InstanceBox.SelectedItem is not InstancePick pick)
            return;
        InstanceContext.Select(pick.Id);
        Load();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Load();

    private void OnKeywordChanged(object sender, TextChangedEventArgs e) => Load();

    private void OnOpenDiag(object sender, RoutedEventArgs e) => AppServices.Nav.Navigate("tools.diag");

    private void Load()
    {
        if (InstanceContext.CurrentId.Length == 0)
        {
            StatusText.Text = "尚未安装实例 · 请先安装一个版本";
            LogText.Text = "没有可显示的日志。";
            return;
        }

        var file = LogReader.LatestInstanceLog(InstanceContext.CurrentId);
        if (file is null)
        {
            StatusText.Text = $"实例 {InstanceContext.CurrentId} 还没有运行日志 · 启动一次游戏后即可查看";
            LogText.Text = "latest.log 尚未生成。";
            return;
        }

        var tail = LogReader.ReadTail(file, 2000, KeywordBox.Text.Trim());
        if (tail is null)
        {
            StatusText.Text = "日志读取失败(文件可能正被游戏占用)";
            LogText.Text = "";
            return;
        }

        LogText.Text = tail.Lines.Count == 0
            ? "没有匹配的日志行。"
            : string.Join(Environment.NewLine, tail.Lines);
        StatusText.Text = $"实例 {InstanceContext.CurrentId} · {tail.Lines.Count} 行 · 警告 {tail.WarnCount} · 错误 {tail.ErrorCount}";
    }
}