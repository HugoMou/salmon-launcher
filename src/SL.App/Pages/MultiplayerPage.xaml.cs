using System.Collections.ObjectModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SL.Core.Logging;
using SL.Core.Net;
using SL.App.Infrastructure;
using SL.App.Notify;
using Windows.ApplicationModel.DataTransfer;

namespace SL.App.Pages;

/// <summary>局域网发现的开放世界行。</summary>
public sealed class McLanRow
{
    public McLanRow(McLanWorld world, Action<McLanWorld> copy)
    {
        Motd = world.Motd;
        Address = $"{world.Address}:{world.Port}";
        CopyCommand = new RelayCommand(() => copy(world));
    }

    public string Motd { get; }
    public string Address { get; }
    public RelayCommand CopyCommand { get; }
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
/// 联机页:三种接入方式都基于真实的 TCP 端口转发。
/// - 自建中继:把本机服务器端口(25565)转发到对外端口,好友直连;
/// - 局域网:监听 Minecraft 原生组播公告,列出同网段已开放的世界;
/// - 添加中继服务器:连别人开的中继,在本机再开一个本地端口供游戏连接。
/// </summary>
public sealed partial class MultiplayerPage : Page
{
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private readonly ObservableCollection<string> _messages = [];
    private readonly ObservableCollection<McLanRow> _worlds = [];
    private readonly ObservableCollection<SavedRelayRow> _saved = [];

    private TcpForwarder? _hostForwarder;
    private TcpForwarder? _manualForwarder;
    private McLanScanner? _scanner;

    public MultiplayerPage()
    {
        InitializeComponent();
        LogList.ItemsSource = _messages;
        LanList.ItemsSource = _worlds;
        SavedList.ItemsSource = _saved;
        ModeHost.IsChecked = true;
        HintText.Text = "中继只转发 TCP 流量;跨网联机需要把对外端口在路由器上做端口映射,或让双方都接入同一个中继。";
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        StatusText.Text = "选择一种联机方式:自建中继、局域网发现或添加中继服务器";
        RefreshSaved();
        AddLog("联机模块就绪 · 玩家名 " + AppServices.Settings.Current.AccountName);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        StopHost();
        StopManualForward();
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
        if (_hostForwarder is not null)
        {
            StopHost();
            return;
        }

        var localPort = ReadPort(LocalPortBox, 25565);
        var listenPort = ReadPort(HostPortBox, 0);
        var forwarder = new TcpForwarder();
        forwarder.Log += text => _ui.TryEnqueue(() => AddLog("[中继] " + text));
        forwarder.StatsChanged += () => _ui.TryEnqueue(UpdateHostInfo);

        if (!forwarder.Start(listenPort, "127.0.0.1", localPort, out var error))
        {
            forwarder.Dispose();
            StatusText.Text = "中继启动失败:" + error;
            AppLog.Warn("Multiplayer", $"自建中继启动失败(本地 {localPort} → 对外 {listenPort}):{error}");
            AppServices.Notify.Publish(NotifyKind.Error, "中继启动失败:" + error);
            return;
        }

        _hostForwarder = forwarder;
        HostStartButton.Content = "停止中继";
        HostInfoPanel.Visibility = Visibility.Visible;
        UpdateHostInfo();
        StatusText.Text = $"中继运行中 · {LocalAddress()}:{forwarder.ListenPort} → 本机 {localPort}";
        AppLog.Info("Multiplayer", $"自建中继已启动:{forwarder.ListenPort} → 127.0.0.1:{localPort}");
        AppServices.Notify.Publish(NotifyKind.Success, "中继已启动,把地址发给好友即可");
    }

    private void UpdateHostInfo()
    {
        if (_hostForwarder is not { Active: true } forwarder)
            return;
        HostEndpointText.Text = $"{LocalAddress()}:{forwarder.ListenPort}";
        HostMemberText.Text =
            $"在线 {forwarder.ConnectionCount} 人 · 上行 {FormatBytes(forwarder.UpBytes)} · "
            + $"下行 {FormatBytes(forwarder.DownBytes)} · 转发到 {forwarder.Target}";
    }

    private void OnCopyEndpoint(object sender, RoutedEventArgs e)
    {
        if (HostEndpointText.Text.Length == 0)
            return;
        Copy(HostEndpointText.Text);
        AppServices.Notify.Publish(NotifyKind.Success, "地址已复制:" + HostEndpointText.Text);
    }

    private void StopHost()
    {
        if (_hostForwarder is null)
            return;
        _hostForwarder.Dispose();
        _hostForwarder = null;
        HostStartButton.Content = "启动中继";
        HostInfoPanel.Visibility = Visibility.Collapsed;
        HostEndpointText.Text = "";
        StatusText.Text = "中继已停止";
    }

    /// <summary>本机在局域网内的 IPv4(优先非回环、非虚拟网卡)。</summary>
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
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork
                        && !IPAddress.IsLoopback(addr.Address))
                        return addr.Address.ToString();
                }
            }
        }
        catch (Exception) { }
        return "127.0.0.1";
    }

    // ------------------------------------------------------------ 模式二:局域网

    private void StartScanner()
    {
        if (_scanner is not null)
            return;
        var scanner = new McLanScanner();
        scanner.Found += world => _ui.TryEnqueue(() => UpsertWorld(world));
        scanner.Failed += reason => _ui.TryEnqueue(() =>
        {
            AddLog("[局域网] " + reason);
            StatusText.Text = reason;
        });
        scanner.Start();
        _scanner = scanner;
        AddLog("[局域网] 正在监听 224.0.2.60:4445 的开放世界公告…");
    }

    private void StopScanner()
    {
        _scanner?.Dispose();
        _scanner = null;
    }

    private void UpsertWorld(McLanWorld world)
    {
        if (_worlds.Any(r => r.Address == $"{world.Address}:{world.Port}"))
            return;
        _worlds.Add(new McLanRow(world, CopyWorld));
        StatusText.Text = $"局域网发现 {_worlds.Count} 个开放世界";
        AddLog($"[局域网] 发现 {world.Motd} · {world.Address}:{world.Port}");
    }

    private void CopyWorld(McLanWorld world)
    {
        Copy($"{world.Address}:{world.Port}");
        AppServices.Notify.Publish(NotifyKind.Success, $"地址已复制:{world.Address}:{world.Port}");
        AddLog($"[复制] {world.Address}:{world.Port} · 在游戏「多人游戏 → 直接连接」里粘贴即可");
    }

    private void OnRescanLan(object sender, RoutedEventArgs e)
    {
        _worlds.Clear();
        StopScanner();
        StartScanner();
        StatusText.Text = "正在重新扫描局域网…";
    }

    // ------------------------------------------------------------ 模式三:添加中继服务器

    private void OnConnectManual(object sender, RoutedEventArgs e)
    {
        var host = ManualHostBox.Text.Trim();
        if (host.Length == 0)
        {
            _ = AppServices.Dialogs.AlertAsync("地址不能为空",
                "请填写中继服务器的 IP 或域名。", Dialogs.MsgMark.Warning);
            return;
        }

        var port = ReadPort(ManualPortBox, 25565);
        var name = ManualNameBox.Text.Trim();
        RelayBook.Add(new RelayEndpoint(name.Length > 0 ? name : host, host, port));
        RefreshSaved();
        StartManualForward(host, port, ReadPort(ManualLocalPortBox, 25565));
    }

    private void StartManualForward(string host, int port, int localPort)
    {
        StopManualForward();

        var forwarder = new TcpForwarder();
        forwarder.Log += text => _ui.TryEnqueue(() => AddLog("[远端] " + text));
        forwarder.StatsChanged += () => _ui.TryEnqueue(UpdateManualStatus);
        if (!forwarder.Start(localPort, host, port, out var error))
        {
            forwarder.Dispose();
            StatusText.Text = "接入失败:" + error;
            AppLog.Warn("Multiplayer", $"接入中继失败({host}:{port} → 本地 {localPort}):{error}");
            AppServices.Notify.Publish(NotifyKind.Error, "接入失败:" + error);
            return;
        }

        _manualForwarder = forwarder;
        UpdateManualStatus();
        AppLog.Info("Multiplayer", $"接入中继 {host}:{port} → 本地 {forwarder.ListenPort}");
        AppServices.Notify.Publish(NotifyKind.Success, $"已接入,游戏里连接 127.0.0.1:{forwarder.ListenPort}");
    }

    private void UpdateManualStatus()
    {
        if (_manualForwarder is not { Active: true } forwarder)
            return;
        StatusText.Text =
            $"已接入 {forwarder.Target} · 游戏里连接 127.0.0.1:{forwarder.ListenPort}"
            + $" · 在线 {forwarder.ConnectionCount}";
    }

    private void StopManualForward()
    {
        _manualForwarder?.Dispose();
        _manualForwarder = null;
    }

    private void RefreshSaved()
    {
        _saved.Clear();
        foreach (var endpoint in RelayBook.List())
        {
            _saved.Add(new SavedRelayRow(endpoint,
                e => StartManualForward(e.Host, e.Port, ReadPort(ManualLocalPortBox, 25565)),
                RemoveSaved));
        }
    }

    private void RemoveSaved(RelayEndpoint endpoint)
    {
        RelayBook.Remove(endpoint.Key);
        RefreshSaved();
        AddLog("[收藏] 已移除 " + endpoint.Display);
    }

    // ------------------------------------------------------------ 断开

    private void OnDisconnect(object sender, RoutedEventArgs e)
    {
        StopHost();
        StopManualForward();
        _worlds.Clear();
        AddLog("[系统] 已断开全部联机通道");
        StatusText.Text = "已断开全部联机通道";
    }

    // ------------------------------------------------------------ 基础设施

    private static int ReadPort(NumberBox box, int fallback)
    {
        // 用户可能刚输入完数值、还没离开输入框(Value 尚未提交),优先读输入框文本
        if (double.TryParse(box.Text, out var typed))
            return (int)Math.Clamp(typed, box.Minimum, box.Maximum);
        if (!double.IsNaN(box.Value))
            return (int)Math.Clamp(box.Value, box.Minimum, box.Maximum);
        return fallback;
    }

    private static void Copy(string text)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(text);
        Clipboard.SetContent(package);
        Clipboard.Flush();
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F2} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F1} MB",
        >= 1L << 10 => $"{bytes / 1024.0:F0} KB",
        _ => bytes + " B",
    };

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
