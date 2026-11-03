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
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using SL.Core.Account;
using SL.Core.Logging;
using SL.App.Localization;
using SL.Core.Skins;
using SL.App.Dialogs;
using SL.App.Infrastructure;
using SL.App.Notify;

namespace SL.App.Pages;

/// <summary>Sidebar user entry (from the local account repository).</summary>
public sealed class SkinUserRow
{
    public SkinUserRow(string id, string name, string detail, string glyph)
    {
        Id = id;
        Name = name;
        Detail = detail;
        Glyph = glyph;
    }

    public string Id { get; }
    public string Name { get; }
    public string Detail { get; }
    public string Glyph { get; }
}

/// <summary>Skin list entry.</summary>
public sealed class SkinRow
{
    public SkinRow(SkinItem item)
    {
        Item = item;
        Name = item.Name;
        Detail = $"{item.SizeText} · {item.ModelText} · {item.SavedText}";
    }

    public SkinItem Item { get; }
    public string Name { get; }
    public string Detail { get; }
}

/// <summary>
/// Skin station: the left side is grouped by user (account); selecting a user shows their saved skins;
/// the right side previews the selected skin as a 3D block model that can be dragged to rotate.
/// </summary>
public sealed partial class SkinStationPage : Page
{
    private readonly ObservableCollection<SkinUserRow> _users = [];
    private readonly ObservableCollection<SkinRow> _skins = [];
    private SkinUserRow? _currentUser;

    public SkinStationPage()
    {
        InitializeComponent();
        UserList.ItemsSource = _users;
        SkinList.ItemsSource = _skins;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadUsers();
    }

    private void LoadUsers()
    {
        var keep = _currentUser?.Id;
        _users.Clear();

        var repo = AccountStore.Load();
        foreach (var account in repo.Accounts)
        {
            _users.Add(new SkinUserRow(account.Id, account.Name,
                (account.Kind == "msa" ? Loc.T("正版账户") : Loc.T("离线账户")) + (account.Id == repo.ActiveId ? Loc.T(" · 当前使用") : ""),
                account.Kind == "msa" ? "\uE8EA" : "\uE77B"));
        }

        if (_users.Count == 0)
        {
            var name = AppServices.Settings.Current.AccountName;
            _users.Add(new SkinUserRow("offline:" + name, name, Loc.T("离线账户 · 默认"), "\uE77B"));
        }

        _currentUser = _users.FirstOrDefault(u => u.Id == keep) ?? _users[0];
        UserList.SelectedItem = _currentUser;
        LoadSkins();
    }

    private void LoadSkins()
    {
        _skins.Clear();
        Preview.Clear();
        SkinNameText.Text = Loc.T("未选择皮肤");
        SkinMetaText.Text = "";

        if (_currentUser is null)
        {
            StatusText.Text = Loc.T("没有可用的用户");
            return;
        }

        try
        {
            foreach (var item in SkinLibrary.List(_currentUser.Id))
                _skins.Add(new SkinRow(item));
        }
        catch (Exception ex)
        {
            AppLog.Error("SkinStation", ex);
            StatusText.Text = Loc.T("读取皮肤失败:") + ex.Message;
            return;
        }

        TitleText.Text = string.Format(Loc.T("皮肤站 · {0}"), _currentUser.Name);
        StatusText.Text = _skins.Count == 0
            ? string.Format(Loc.T("{0} 还没有保存的皮肤 · 可从本地添加或按网址下载"), _currentUser.Name)
            : string.Format(Loc.T("{0} 共保存 {1} 个皮肤 · 选中即可 3D 预览"), _currentUser.Name, _skins.Count);

        if (_skins.Count > 0)
            SkinList.SelectedItem = _skins[0];
    }

    private async void OnUserChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UserList.SelectedItem is not SkinUserRow row)
            return;
        _currentUser = row;
        LoadSkins();
        await Task.CompletedTask;
    }

    private async void OnSkinChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SkinList.SelectedItem is not SkinRow row)
            return;
        SkinNameText.Text = row.Name;
        SkinMetaText.Text = string.Format(Loc.T("{0} · {1} · 保存于 {2}"), row.Item.SizeText, row.Item.ModelText, row.Item.SavedText);
        await Preview.LoadAsync(row.Item.Path);
    }

    private void OnResetView(object sender, RoutedEventArgs e)
    {
        if (SkinList.SelectedItem is SkinRow row)
            _ = Preview.LoadAsync(row.Item.Path);
    }

    private async void OnImportSkin(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        try
        {
            var picker = new FileOpenPicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.Window));
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".png");

            var file = await picker.PickSingleFileAsync();
            if (file is null)
                return;

            var name = SkinLibrary.Import(_currentUser.Id, file.Path);
            AppServices.Notify.Publish(NotifyKind.Success, Loc.T("已添加皮肤:") + name);
            LoadSkins();
        }
        catch (Exception ex)
        {
            AppLog.Error("SkinStation", ex);
            AppServices.Notify.Publish(NotifyKind.Error, Loc.T("添加失败:") + ex.Message);
        }
    }

    private async void OnDownloadSkin(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var url = await AppServices.Dialogs.PromptAsync(Loc.T("从网址添加皮肤"),
            Loc.T("粘贴皮肤 PNG 的直链(http/https),例如皮肤站提供的纹理地址。")).ConfigureAwait(true);
        if (url is null)
            return;

        try
        {
            StatusText.Text = Loc.T("正在下载皮肤…");
            var name = await SkinLibrary.DownloadAsync(_currentUser.Id, url.Trim(), null,
                CancellationToken.None).ConfigureAwait(true);
            AppLog.Info("SkinStation", $"已下载皮肤 {name}");
            AppServices.Notify.Publish(NotifyKind.Success, Loc.T("已下载皮肤:") + name);
            LoadSkins();
        }
        catch (Exception ex)
        {
            AppLog.Error("SkinStation", ex);
            StatusText.Text = Loc.T("下载失败:") + ex.Message;
            AppServices.Notify.Publish(NotifyKind.Error, Loc.T("下载失败:") + ex.Message);
        }
    }

    private async void OnDeleteSkin(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null || SkinList.SelectedItem is not SkinRow row)
            return;

        if (!await AppServices.Dialogs.ConfirmAsync(Loc.T("删除皮肤"),
                string.Format(Loc.T("将删除「{0}」,该操作不可撤销。"), row.Name), MsgMark.Warning).ConfigureAwait(true))
            return;

        try
        {
            SkinLibrary.Delete(_currentUser.Id, row.Item.FileName);
            AppServices.Notify.Publish(NotifyKind.Info, Loc.T("已删除:") + row.Name);
            LoadSkins();
        }
        catch (Exception ex)
        {
            AppLog.Error("SkinStation", ex);
            AppServices.Notify.Publish(NotifyKind.Error, Loc.T("删除失败:") + ex.Message);
        }
    }

    private void OnOpenDir(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;
        try
        {
            var dir = SkinLibrary.DirOf(_currentUser.Id);
            Directory.CreateDirectory(dir);
            Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLog.Warn("SkinStation", "打开目录失败:" + ex.Message);
        }
    }

    private void OnManageAccounts(object sender, RoutedEventArgs e)
        => AppServices.Nav.Navigate("st.launch");
}