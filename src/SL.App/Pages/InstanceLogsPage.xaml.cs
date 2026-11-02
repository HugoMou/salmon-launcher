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
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SL.Core.Logging;
using SL.Core.Tools;
using SL.App.Localization;
using SL.App.Instances;
using SL.App.Infrastructure;

namespace SL.App.Pages;

/// <summary>Instance logs page: reads the tail of latest.log and filters by keyword.</summary>
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
            StatusText.Text = Loc.T("尚未安装实例 · 请先安装一个版本");
            LogText.Text = Loc.T("没有可显示的日志。");
            return;
        }

        var file = LogReader.LatestInstanceLog(InstanceContext.CurrentId);
        if (file is null)
        {
            StatusText.Text = string.Format(Loc.T("实例 {0} 还没有运行日志 · 启动一次游戏后即可查看"), InstanceContext.CurrentId);
            LogText.Text = Loc.T("latest.log 尚未生成。");
            return;
        }

        var tail = LogReader.ReadTail(file, 2000, KeywordBox.Text.Trim());
        if (tail is null)
        {
            StatusText.Text = Loc.T("日志读取失败(文件可能正被游戏占用)");
            LogText.Text = "";
            return;
        }

        LogText.Text = tail.Lines.Count == 0
            ? Loc.T("没有匹配的日志行。")
            : string.Join(Environment.NewLine, tail.Lines);
        StatusText.Text = string.Format(Loc.T("实例 {0} · {1} 行 · 警告 {2} · 错误 {3}"), InstanceContext.CurrentId, tail.Lines.Count, tail.WarnCount, tail.ErrorCount);
    }
}