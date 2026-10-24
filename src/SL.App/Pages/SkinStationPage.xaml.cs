using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SL.Core.Account;
using SL.Core.Logging;
using SL.Core.Skins;
using SL.App.Dialogs;
using SL.App.Infrastructure;
using SL.App.Notify;

namespace SL.App.Pages;

/// <summary>侧边栏用户条目(来自本机账户仓库)。</summary>
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

/// <summary>皮肤列表条目。</summary>
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
/// 皮肤站:左侧按用户(账户)分栏,选中用户即可查看其保存的皮肤;
/// 右侧对所选皮肤做 3D 方块模型预览,可拖动旋转。
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
                (account.Kind == "msa" ? "正版账户" : "离线账户") + (account.Id == repo.ActiveId ? " · 当前使用" : ""),
                account.Kind == "msa" ? "\uE8EA" : "\uE77B"));
        }

        if (_users.Count == 0)
        {
            var name = AppServices.Settings.Current.AccountName;
            _users.Add(new SkinUserRow("offline:" + name, name, "离线账户 · 默认", "\uE77B"));
        }

        _currentUser = _users.FirstOrDefault(u => u.Id == keep) ?? _users[0];
        UserList.SelectedItem = _currentUser;
        LoadSkins();
    }

    private void LoadSkins()
    {
        _skins.Clear();
        Preview.Clear();
        SkinNameText.Text = "未选择皮肤";
        SkinMetaText.Text = "";

        if (_currentUser is null)
        {
            StatusText.Text = "没有可用的用户";
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
            StatusText.Text = "读取皮肤失败:" + ex.Message;
            return;
        }

        TitleText.Text = $"皮肤站 · {_currentUser.Name}";
        StatusText.Text = _skins.Count == 0
            ? $"{_currentUser.Name} 还没有保存的皮肤 · 可从本地添加或按网址下载"
            : $"{_currentUser.Name} 共保存 {_skins.Count} 个皮肤 · 选中即可 3D 预览";

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
        SkinMetaText.Text = $"{row.Item.SizeText} · {row.Item.ModelText} · 保存于 {row.Item.SavedText}";
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

        var path = await AppServices.Dialogs.PromptAsync("添加本地皮肤",
            "粘贴 PNG 皮肤文件的完整路径(64×64 现代皮肤或 64×32 旧版皮肤)。").ConfigureAwait(true);
        if (path is null)
            return;

        try
        {
            var name = SkinLibrary.Import(_currentUser.Id, path.Trim('"', ' '));
            AppServices.Notify.Publish(NotifyKind.Success, "已添加皮肤:" + name);
            LoadSkins();
        }
        catch (Exception ex)
        {
            AppLog.Error("SkinStation", ex);
            AppServices.Notify.Publish(NotifyKind.Error, "添加失败:" + ex.Message);
        }
    }

    private async void OnDownloadSkin(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null)
            return;

        var url = await AppServices.Dialogs.PromptAsync("从网址添加皮肤",
            "粘贴皮肤 PNG 的直链(http/https),例如皮肤站提供的纹理地址。").ConfigureAwait(true);
        if (url is null)
            return;

        try
        {
            StatusText.Text = "正在下载皮肤…";
            var name = await SkinLibrary.DownloadAsync(_currentUser.Id, url.Trim(), null,
                CancellationToken.None).ConfigureAwait(true);
            AppLog.Info("SkinStation", $"已下载皮肤 {name}");
            AppServices.Notify.Publish(NotifyKind.Success, "已下载皮肤:" + name);
            LoadSkins();
        }
        catch (Exception ex)
        {
            AppLog.Error("SkinStation", ex);
            StatusText.Text = "下载失败:" + ex.Message;
            AppServices.Notify.Publish(NotifyKind.Error, "下载失败:" + ex.Message);
        }
    }

    private async void OnDeleteSkin(object sender, RoutedEventArgs e)
    {
        if (_currentUser is null || SkinList.SelectedItem is not SkinRow row)
            return;

        if (!await AppServices.Dialogs.ConfirmAsync("删除皮肤",
                $"将删除「{row.Name}」,该操作不可撤销。", MsgMark.Warning).ConfigureAwait(true))
            return;

        try
        {
            SkinLibrary.Delete(_currentUser.Id, row.Item.FileName);
            AppServices.Notify.Publish(NotifyKind.Info, "已删除:" + row.Name);
            LoadSkins();
        }
        catch (Exception ex)
        {
            AppLog.Error("SkinStation", ex);
            AppServices.Notify.Publish(NotifyKind.Error, "删除失败:" + ex.Message);
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