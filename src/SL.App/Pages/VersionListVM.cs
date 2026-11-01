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
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SL.Core.Logging;
using SL.Core.Net;
using SL.App.Download;
using SL.App.Infrastructure;
using SL.App.Notify;

namespace SL.App.Pages;

/// <summary>版本清单中的一行。</summary>
public sealed class VersionRow
{
    public VersionRow(MojangMeta.VersionEntry entry)
    {
        Entry = entry;
        Id = entry.Id;
        Badge = entry.Badge;
        Released = entry.ReleaseTime == default ? "" : entry.ReleaseTime.LocalDateTime.ToString("yyyy-MM-dd");
    }

    public MojangMeta.VersionEntry Entry { get; }
    public string Id { get; }
    public string Badge { get; }
    public string Released { get; }

    public override string ToString() => Id;
}

/// <summary>游戏版本页视图模型:Mojang 清单拉取、类型筛选、搜索与选择。</summary>
public sealed class VersionListVM : VmBase, IRefreshable
{
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private IReadOnlyList<VersionRow> _all = [];
    private bool _loading;

    public VersionListVM()
    {
        RefreshCommand = new RelayCommand(async () => await LoadAsync(force: true));
        ChooseCommand = new RelayCommand(Choose, () => Selected is not null);
        _ = LoadAsync(force: false);
    }

    public ObservableCollection<VersionRow> Rows { get; } = [];

    public RelayCommand RefreshCommand { get; }
    public RelayCommand ChooseCommand { get; }

    private VersionRow? _selected;
    public VersionRow? Selected
    {
        get => _selected;
        set
        {
            if (Set(ref _selected, value))
                ChooseCommand.NotifyCanExecuteChanged();
        }
    }

    private string _status = "";

    private string _keyword = "";
    public string Keyword
    {
        get => _keyword;
        set
        {
            if (Set(ref _keyword, value))
            {
                ApplyFilter();
                On(nameof(StatusText));
            }
        }
    }

    private int _typeIndex;
    public int TypeIndex
    {
        get => _typeIndex;
        set
        {
            if (Set(ref _typeIndex, value))
            {
                ApplyFilter();
                On(nameof(StatusText));
            }
        }
    }

    public Visibility BusyVisibility => _loading ? Visibility.Visible : Visibility.Collapsed;

    public string StatusText => _status.Length > 0
        ? _status
        : _loading
            ? "正在读取版本清单…"
            : $"共 {Rows.Count} 个版本" + (InstallWizard.HasVersion ? $" · 已选 {InstallWizard.VersionId}" : "");

    private void SetStatus(string text)
    {
        _status = text;
        On(nameof(StatusText));
    }

    public void RefreshData() => ChooseCommand.NotifyCanExecuteChanged();

    private async Task LoadAsync(bool force)
    {
        if (_loading)
            return;
        _loading = true;
        On(nameof(BusyVisibility));
        On(nameof(StatusText));
        try
        {
            var list = await MojangMeta.GetVersionsAsync(force, CancellationToken.None).ConfigureAwait(true);
            _all = list.Select(v => new VersionRow(v)).ToList();
            _status = "";
            ApplyFilter();
            AppLog.Info("Download", $"版本清单已加载:{list.Count} 项");
        }
        catch (Exception ex)
        {
            AppLog.Error("Download", ex);
            SetStatus("版本清单读取失败:" + ex.Message);
            AppServices.Notify.Publish(NotifyKind.Error, "版本清单读取失败:" + ex.Message);
        }
        finally
        {
            _loading = false;
            On(nameof(BusyVisibility));
            On(nameof(StatusText));
        }
    }

    private void ApplyFilter()
    {
        var keyword = _keyword.Trim();
        IEnumerable<VersionRow> query = _all;

        query = _typeIndex switch
        {
            1 => query.Where(v => v.Entry.Type == "release"),
            2 => query.Where(v => v.Entry.Type == "snapshot"),
            3 => query.Where(v => v.Entry.Type is not ("release" or "snapshot")),
            _ => query,
        };

        if (keyword.Length > 0)
            query = query.Where(v => v.Id.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        var keep = _selected?.Id;
        Rows.Clear();
        foreach (var row in query)
            Rows.Add(row);

        Selected = Rows.FirstOrDefault(r => r.Id == keep) ?? Rows.FirstOrDefault();
        _ui.TryEnqueue(() =>
        {
            On(nameof(StatusText));
            ChooseCommand.NotifyCanExecuteChanged();
        });
    }

    private void Choose()
    {
        if (_selected is null)
            return;
        InstallWizard.ChooseVersion(_selected.Entry);
        AppServices.Notify.Publish(NotifyKind.Info, $"已选择 Minecraft {_selected.Id},请选择加载器");
        AppServices.Nav.Navigate("dl.loaders");
    }
}