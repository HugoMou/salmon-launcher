using System.Collections.ObjectModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SL.Core.Logging;
using SL.Core.Net;
using SL.App.Infrastructure;
using SL.App.Notify;

namespace SL.App.Pages;

/// <summary>局域网房间行。</summary>
public sealed class LanRoomRow
{
    public LanRoomRow(LanRoom room, Action<LanRoom> join)
    {
        Room = room.Room;
        Detail = $"{room.HostName} · {room.Address}:{room.Port}";
        JoinCommand = new RelayCommand(() => join(room));
    }

    public string Room { get; }
    public string Detail { get; }
    public RelayCommand JoinCommand { get; }
}

/// <summary>收藏的中继服务器行。</summary>
public sealed class SavedRelayRow
{
    public SavedRelayRow(RelayEndpoint endpoint, Action<RelayEndpoint> connect, Action<RelayEndpoint> remove)
    {
        Endpoint = endpoint;
        Display = endpoint.Display;
        ConnectCommand = new RelayCommand(() => connect(endpoint));
        RemoveCommand = new RelayCommand(() => remove(endpoint));
    }

    public RelayEndpoint Endpoint { get; }
    public string Display { get; }
    public RelayCommand ConnectCommand { get; }
    public RelayCommand RemoveCommand { get; }
}

/// <summary>
/// 联机页:三种接入方式 —— 本机当自建中继、局域网自动发现、手动添加中继服务器。
/// 底层都是同一套 TCP 行协议(VoxLinkRelay / VoxLinkRelayClient),消息真实收发。
/// </summary>
public sealed partial class MultiplayerPage : Page
{
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private readonly ObservableCollection<string> _messages = [];
    private readonly ObservableCollection<LanRoomRow> _rooms = [];
    private readonly ObservableCollection<SavedRelayRow> _saved = [];

    private VoxLinkRelay? _relay;
    private VoxLinkLanBeacon? _beacon;
    private VoxLinkLanScanner? _scanner;
    private VoxLinkRelayClient? _client;

    private string MyName => AppServices.Settings.Current.AccountName;

    public MultiplayerPage()
    {
        InitializeComponent();
        LogList.ItemsSource = _messages;
        LanList.ItemsSource = _rooms;
        SavedList.ItemsSource = _saved;
        ModeHost.IsChecked = true;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        StatusText.Text = "选择一种联机方式:自建中继、局域网发现或手动添加中继服务器";
        RefreshSaved();
        AddLog("联机模块就绪 · 当前玩家名 " + MyName);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        StopHost(silent: true);
        StopScanner();
    }

    // ------------------------------------------------------------ 模式切换

    private void OnModeChecked(object sender, RoutedEventArgs e)
    {
        if (HostPanel is null)
            return;
        var host = ReferenceEquals(sender, ModeHost);
        var lan = ReferenceEquals(sender, ModeLan);
        HostPanel.Visibility = host ? Visibility.Visible : Visibility.Collapsed;
        LanPanel.Visibility = lan ? Visibility.Visible : Visibility.Collapsed;
        ManualPanel.Visibility = host || lan ? Visibility.Collapsed : Visibility.Visible;

        if (lan)
            StartScanner();
        else
            StopScanner();
    }

    // ------------------------------------------------------------ 模式一:自建中继

    private void OnStartHost(object sender, RoutedEventArgs e)
    {
        if (_relay is { Active: true })
        {
            StopHost(silent: false);
            return;
        }

        var port = double.IsNaN(HostPortBox.Value) ? 0 : (int)HostPortBox.Value;
        var room = RoomNameBox.Text.Trim();
        if (room.Length == 0)
            room = "我的房间";

        var relay = new VoxLinkRelay();
        relay.Message += text => _ui.TryEnqueue(() => AddLog(text));
        relay.System += text => _ui.TryEnqueue(() => AddLog("[中继] " + text));
        relay.MemberJoined += name => _ui.TryEnqueue(() =>
        {
            UpdateHostInfo();
            AddLog("[成员] " + name + " 已加入");
        });
        relay.MemberLeft += name => _ui.TryEnqueue(() =>
        {
            UpdateHostInfo();
            AddLog("[成员] " + name + " 已离开");
        });

        if (!relay.Start(port, room))
        {
            relay.Dispose();
            AppServices.Notify.Publish(NotifyKind.Error, "中继启动失败,端口可能被占用");
            return;
        }

        _relay = relay;
        _beacon = new VoxLinkLanBeacon();
        _beacon.Start(relay.Port, room, MyName);

        HostStartButton.Content = "停止中继";
        HostInfoPanel.Visibility = Visibility.Visible;
        UpdateHostInfo();
        StatusText.Text = $"中继运行中 · 端口 {relay.Port} · 房间 {room}";
        AppLog.Info("Multiplayer", $"自建中继已启动:端口 {relay.Port}");
        AppServices.Notify.Publish(NotifyKind.Success, "中继已启动,邀请码见下方");
    }

    private void UpdateHostInfo()
    {
        if (_relay is not { Active: true })
            return;
        var address = LocalAddress();
        HostEndpointText.Text = $"{address}:{_relay.Port}   (邀请码 {_relay.InviteCode})";
        HostMemberText.Text = $"当前成员 {_relay.MemberCount} 人 · 房间 {_relay.RoomName}";
    }

    private static string LocalAddress()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up
                    || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;
                foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                        return addr.Address.ToString();
                }
            }
        }
        catch (Exception) { }
        return "127.0.0.1";
    }

    private void StopHost(bool silent)
    {
        _beacon?.Dispose();
        _beacon = null;
        _relay?.Dispose();
        _relay = null;
        HostStartButton.Content = "启动中继";
        HostInfoPanel.Visibility = Visibility.Collapsed;
        if (!silent)
            StatusText.Text = "中继已停止";
    }

    // ------------------------------------------------------------ 模式二:局域网

    private void StartScanner()
    {
        if (_scanner is not null)
            return;
        var scanner = new VoxLinkLanScanner();
        scanner.Found += room => _ui.TryEnqueue(() => UpsertRoom(room));
        scanner.Start();
        _scanner = scanner;
        AddLog("[局域网] 正在监听同网段广播…");
    }

    private void StopScanner()
    {
        _scanner?.Dispose();
        _scanner = null;
    }

    private void UpsertRoom(LanRoom room)
    {
        if (_rooms.Any(r => r.Detail.EndsWith($"{room.Address}:{room.Port}", StringComparison.Ordinal)))
            return;
        _rooms.Add(new LanRoomRow(room, JoinLanRoom));
        StatusText.Text = $"局域网发现 {_rooms.Count} 个房间";
    }

    private void OnRescanLan(object sender, RoutedEventArgs e)
    {
        _rooms.Clear();
        StopScanner();
        StartScanner();
        StatusText.Text = "正在重新扫描局域网…";
    }

    private void JoinLanRoom(LanRoom room)
    {
        _ = ConnectAsync(room.Address, room.Port, room.Room);
    }

    // ------------------------------------------------------------ 模式三:手动中继

    private async void OnConnectManual(object sender, RoutedEventArgs e)
    {
        var host = ManualHostBox.Text.Trim();
        if (host.Length == 0)
        {
            await AppServices.Dialogs.AlertAsync("地址不能为空",
                "请填写中继服务器的 IP 或域名。", SL.App.Dialogs.MsgMark.Warning).ConfigureAwait(true);
            return;
        }

        var port = double.IsNaN(ManualPortBox.Value) ? 25565 : (int)ManualPortBox.Value;
        var name = ManualNameBox.Text.Trim();
        RelayBook.Add(new RelayEndpoint(name.Length > 0 ? name : host, host, port));
        RefreshSaved();
        await ConnectAsync(host, port, name).ConfigureAwait(true);
    }

    private void RefreshSaved()
    {
        _saved.Clear();
        foreach (var endpoint in RelayBook.List())
            _saved.Add(new SavedRelayRow(endpoint, e => _ = ConnectAsync(e.Host, e.Port, e.Name), RemoveSaved));
    }

    private void RemoveSaved(RelayEndpoint endpoint)
    {
        RelayBook.Remove(endpoint.Key);
        RefreshSaved();
        AddLog("[收藏] 已移除 " + endpoint.Display);
    }

    // ------------------------------------------------------------ 客户端连接

    private async Task ConnectAsync(string host, int port, string roomOrName)
    {
        StopClient();
        var client = new VoxLinkRelayClient();
        client.Message += text => _ui.TryEnqueue(() => AddLog(text));
        client.System += text => _ui.TryEnqueue(() => AddLog("[房间] " + text));
        client.Disconnected += () => _ui.TryEnqueue(() => AddLog("[房间] 与服务端断开连接"));

        StatusText.Text = $"正在连接 {host}:{port} …";
        try
        {
            await client.ConnectAsync(host, port, MyName, CancellationToken.None).ConfigureAwait(true);
            _client = client;
            StatusText.Text = $"已连接到 {host}:{port}" + (roomOrName.Length > 0 ? $" · {roomOrName}" : "");
            AddLog($"[连接] 已加入 {host}:{port}");
            AppLog.Info("Multiplayer", $"已连接中继 {host}:{port}");
            AppServices.Notify.Publish(NotifyKind.Success, "已加入联机房间");
        }
        catch (Exception ex)
        {
            client.Dispose();
            AppLog.Error("Multiplayer", ex);
            StatusText.Text = "连接失败:" + ex.Message;
            AppServices.Notify.Publish(NotifyKind.Error, "连接失败:" + ex.Message);
        }
    }

    private void StopClient()
    {
        _client?.Dispose();
        _client = null;
    }

    private void OnDisconnect(object sender, RoutedEventArgs e)
    {
        StopClient();
        StopHost(silent: false);
        AddLog("[系统] 已断开所有联机连接");
    }

    // ------------------------------------------------------------ 消息

    private void OnMessageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
            return;
        SendCurrent(MessageBox.Text);
    }

    private void OnSend(object sender, RoutedEventArgs e) => SendCurrent(MessageBox.Text);

    private void SendCurrent(string text)
    {
        var body = text.Trim();
        if (body.Length == 0)
            return;

        if (_relay is { Active: true })
        {
            _relay.Say(MyName, body);
            MessageBox.Text = "";
            return;
        }

        if (_client is { Connected: true })
        {
            _ = _client.SendAsync(body);
            AddLog($"{MyName}: {body}");
            MessageBox.Text = "";
            return;
        }

        AppServices.Notify.Publish(NotifyKind.Warning, "尚未连接房间,请先启动中继或加入房间");
    }

    private void AddLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;
        _messages.Add($"{DateTime.Now:HH:mm:ss}  {text}");
        if (_messages.Count > 400)
            _messages.RemoveAt(0);
        LogScroll.ChangeView(null, LogScroll.ScrollableHeight, null);
    }
}