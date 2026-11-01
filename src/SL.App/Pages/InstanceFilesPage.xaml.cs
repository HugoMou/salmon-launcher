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

/// <summary>实例内文件行(资源包 / 光影 / 存档共用)。</summary>
public sealed class InstanceFileRow
{
    public InstanceFileRow(string name, string detail, string glyph, string actionText, string path)
    {
        Name = name;
        Detail = detail;
        Glyph = glyph;
        ActionText = actionText;
        Path = path;
    }

    public string Name { get; }
    public string Detail { get; }
    public string Glyph { get; }
    public string ActionText { get; }
    public string Path { get; }
    public Visibility ActionVisibility
        => ActionText.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>实例文件页:资源包 / 光影 / 存档三种视图共用,分类由路由 key 决定。</summary>
public sealed partial class InstanceFilesPage : Page
{
    private readonly ObservableCollection<InstancePick> _picks = [];
    private readonly ObservableCollection<InstanceFileRow> _rows = [];

    private string _routeKey = "inst.packs";
    private string _subDir = "resourcepacks";
    private bool _isSaves;
    private bool _syncing;

    public InstanceFilesPage()
    {
        InitializeComponent();
        InstanceBox.ItemsSource = _picks;
        FileList.ItemsSource = _rows;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _routeKey = e.Parameter as string ?? "inst.packs";
        (_subDir, _isSaves, TitleText.Text) = _routeKey switch
        {
            "inst.shaders" => ("shaderpacks", false, "光影"),
            "inst.saves" => ("saves", true, "存档"),
            _ => ("resourcepacks", false, "资源包"),
        };
        BackupButton.Visibility = _isSaves ? Visibility.Visible : Visibility.Collapsed;
        Reload();
    }

    private void Reload()
    {
        _syncing = true;
        var all = InstanceContext.Refresh();
        _picks.Clear();
        foreach (var info in all)
            _picks.Add(new InstancePick(info));
        InstanceBox.SelectedItem = _picks.FirstOrDefault(p => p.Id == InstanceContext.CurrentId);
        _syncing = false;

        _rows.Clear();
        if (InstanceContext.CurrentId.Length == 0)
        {
            StatusText.Text = "尚未安装实例 · 请先安装一个版本";
            return;
        }

        if (_isSaves)
            LoadSaves();
        else
            LoadFiles();
    }

    private void LoadSaves()
    {
        var saves = InstanceRegistry.ListSaves(InstanceContext.CurrentId);
        foreach (var save in saves)
        {
            _rows.Add(new InstanceFileRow(save.Name,
                $"最近游玩 {save.LastPlayed:yyyy-MM-dd HH:mm} · {save.SizeMb} MB",
                "\uE74E",
                $"打开",
                System.IO.Path.Combine(InstanceRegistry.InstanceDirOf(InstanceContext.CurrentId), "saves", save.Name)));
        }

        StatusText.Text = saves.Count == 0
            ? $"实例 {InstanceContext.CurrentId} 暂无存档 · 进入游戏创建世界后会出现在这里"
            : $"实例 {InstanceContext.CurrentId} · 共 {saves.Count} 个存档";
    }

    private void LoadFiles()
    {
        var dir = System.IO.Path.Combine(InstanceRegistry.InstanceDirOf(InstanceContext.CurrentId), _subDir);
        var files = new List<System.IO.FileInfo>();
        if (System.IO.Directory.Exists(dir))
        {
            try
            {
                files.AddRange(System.IO.Directory
                    .EnumerateFiles(dir, "*", System.IO.SearchOption.TopDirectoryOnly)
                    .Select(p => new System.IO.FileInfo(p))
                    .OrderByDescending(f => f.LastWriteTime));
            }
            catch (Exception ex)
            {
                AppLog.Warn("InstanceFiles", "枚举目录失败:" + ex.Message);
            }
        }

        foreach (var file in files)
        {
            var sizeText = file.Length >= 1 << 20
                ? $"{file.Length / (1024.0 * 1024.0):F1} MB"
                : $"{file.Length / 1024.0:F0} KB";
            _rows.Add(new InstanceFileRow(file.Name,
                $"{sizeText} · {file.LastWriteTime:yyyy-MM-dd HH:mm}",
                _isSaves ? "\uE74E" : "\uE8F1",
                "删除",
                file.FullName));
        }

        StatusText.Text = files.Count == 0
            ? $"实例 {InstanceContext.CurrentId} 的 {_subDir} 目录为空 · 可从下载页安装"
            : $"实例 {InstanceContext.CurrentId} · {_subDir} 共 {files.Count} 个文件";
    }

    private void OnInstanceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || InstanceBox.SelectedItem is not InstancePick pick)
            return;
        InstanceContext.Select(pick.Id);
        Reload();
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Reload();

    private async void OnRowAction(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not InstanceFileRow row)
            return;

        if (_isSaves)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = row.Path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppLog.Warn("InstanceFiles", "打开存档失败:" + ex.Message);
            }
            return;
        }

        if (!await AppServices.Dialogs.ConfirmAsync("删除文件",
                $"将删除「{row.Name}」,该操作不可撤销。", MsgMark.Warning).ConfigureAwait(true))
            return;

        try
        {
            System.IO.File.Delete(row.Path);
            AppServices.Notify.Publish(NotifyKind.Info, "已删除:" + row.Name);
        }
        catch (Exception ex)
        {
            AppLog.Error("InstanceFiles", ex);
            AppServices.Notify.Publish(NotifyKind.Error, "删除失败:" + ex.Message);
        }
        Reload();
    }

    private async void OnBackup(object sender, RoutedEventArgs e)
    {
        if (InstanceContext.CurrentId.Length == 0)
            return;
        try
        {
            var path = InstanceRegistry.BackupSaves(InstanceContext.CurrentId);
            if (path is null)
            {
                await AppServices.Dialogs.AlertAsync("没有可备份的存档", "saves 目录为空。", MsgMark.Info)
                    .ConfigureAwait(true);
                return;
            }
            AppServices.Notify.Publish(NotifyKind.Success, "存档已备份:" + System.IO.Path.GetFileName(path));
            StatusText.Text = "已备份到 " + path;
        }
        catch (Exception ex)
        {
            AppLog.Error("InstanceFiles", ex);
            AppServices.Notify.Publish(NotifyKind.Error, "备份失败:" + ex.Message);
        }
    }

    private void OnOpenDir(object sender, RoutedEventArgs e)
    {
        if (InstanceContext.CurrentId.Length == 0)
            return;
        try
        {
            var dir = System.IO.Path.Combine(InstanceRegistry.InstanceDirOf(InstanceContext.CurrentId), _subDir);
            System.IO.Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("InstanceFiles", "打开目录失败:" + ex.Message);
        }
    }
}