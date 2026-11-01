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

/// <summary>模组行(绑定模组列表)。</summary>
public sealed class ModRow
{
    public ModRow(ModInfo info)
    {
        FileName = info.FileName;
        Name = info.BaseName;
        Enabled = info.Enabled;
        Detail = $"{info.SizeBytes / 1024.0:F0} KB · {info.ModifiedAt:yyyy-MM-dd HH:mm}"
                 + (info.Enabled ? "" : " · 已停用");
        Glyph = info.Enabled ? "\uE7B8" : "\uE7BA";
    }

    public string FileName { get; }
    public string Name { get; }
    public bool Enabled { get; }
    public string Detail { get; }
    public string Glyph { get; }
}

/// <summary>模组管理页:启停(改 .disabled 后缀)、删除与从本地导入。</summary>
public sealed partial class InstanceModsPage : Page
{
    private readonly ObservableCollection<InstancePick> _picks = [];
    private readonly ObservableCollection<ModRow> _rows = [];
    private bool _syncing;

    public InstanceModsPage()
    {
        InitializeComponent();
        InstanceBox.ItemsSource = _picks;
        ModList.ItemsSource = _rows;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
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

        foreach (var mod in InstanceRegistry.ListMods(InstanceContext.CurrentId))
            _rows.Add(new ModRow(mod));

        var enabled = _rows.Count(r => r.Enabled);
        StatusText.Text = _rows.Count == 0
            ? $"实例 {InstanceContext.CurrentId} 的 mods 目录为空 · 可从「下载 → 模组仓库」安装"
            : $"实例 {InstanceContext.CurrentId} · 共 {_rows.Count} 个模组,启用 {enabled} 个";
    }

    private void OnInstanceChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || InstanceBox.SelectedItem is not InstancePick pick)
            return;
        InstanceContext.Select(pick.Id);
        Reload();
    }

    private async void OnToggleMod(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleSwitch toggle || toggle.DataContext is not ModRow row)
            return;
        if (InstanceContext.CurrentId.Length == 0)
            return;

        try
        {
            var enabled = InstanceRegistry.ToggleMod(InstanceContext.CurrentId, row.FileName);
            AppServices.Notify.Publish(NotifyKind.Info, $"{(enabled ? "已启用" : "已停用")}:{row.Name}");
        }
        catch (Exception ex)
        {
            AppLog.Error("InstanceMods", ex);
            AppServices.Notify.Publish(NotifyKind.Error, "切换失败:" + ex.Message);
        }
        Reload();
    }

    private async void OnDeleteMod(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not ModRow row)
            return;
        if (InstanceContext.CurrentId.Length == 0)
            return;

        if (!await AppServices.Dialogs.ConfirmAsync("删除模组",
                $"将删除「{row.Name}」,该操作不可撤销。", MsgMark.Warning).ConfigureAwait(true))
            return;

        try
        {
            InstanceRegistry.DeleteMod(InstanceContext.CurrentId, row.FileName);
            AppServices.Notify.Publish(NotifyKind.Info, "已删除:" + row.Name);
        }
        catch (Exception ex)
        {
            AppLog.Error("InstanceMods", ex);
            AppServices.Notify.Publish(NotifyKind.Error, "删除失败:" + ex.Message);
        }
        Reload();
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        if (InstanceContext.CurrentId.Length == 0)
        {
            await AppServices.Dialogs.AlertAsync("尚未安装实例", "请先安装一个实例再导入模组。",
                MsgMark.Info).ConfigureAwait(true);
            return;
        }

        var path = await AppServices.Dialogs.PromptAsync("导入模组",
            "粘贴模组 .jar 的完整路径(同名文件会自动追加序号,不会覆盖)。").ConfigureAwait(true);
        if (path is null)
            return;

        try
        {
            var name = InstanceRegistry.ImportMod(InstanceContext.CurrentId, path.Trim('"'));
            AppServices.Notify.Publish(NotifyKind.Success, "已导入:" + name);
        }
        catch (Exception ex)
        {
            AppLog.Error("InstanceMods", ex);
            AppServices.Notify.Publish(NotifyKind.Error, "导入失败:" + ex.Message);
        }
        Reload();
    }

    private void OnOpenDir(object sender, RoutedEventArgs e)
    {
        if (InstanceContext.CurrentId.Length == 0)
            return;
        try
        {
            var dir = InstanceRegistry.ModsDirOf(InstanceContext.CurrentId);
            System.IO.Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("InstanceMods", "打开目录失败:" + ex.Message);
        }
    }
}