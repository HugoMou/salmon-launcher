using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SL.Core.Net;

/// <summary>
/// 自建中继服务器(TCP 明文行协议,局域网/公网皆可):
/// - 客户端 → 中继:JOIN &lt;名字&gt; / SAY &lt;内容&gt; / PING
/// - 中继 → 客户端:SYS &lt;系统消息&gt; / MSG &lt;名字&gt; &lt;内容&gt;
/// 中继只做转发,不解析游戏数据;房间不落盘。
/// </summary>
public sealed class VoxLinkRelay : IDisposable
{
    private readonly List<RelaySession> _sessions = [];
    private readonly object _gate = new();
    private TcpListener? _listener;

    public event Action<string>? MemberJoined;
    public event Action<string>? MemberLeft;
    public event Action<string>? Message;
    public event Action<string>? System;

    public int Port { get; private set; }
    public bool Active { get; private set; }
    public string RoomName { get; private set; } = "";
    public int MemberCount { get { lock (_gate) return _sessions.Count; } }

    /// <summary>邀请码:SLROOM-&lt;端口36进制&gt;-&lt;房间名长度16进制&gt;。</summary>
    public string InviteCode => Active ? $"SLROOM-{VoxLinkHost.ToBase36(Port)}-{RoomName.Length:X2}" : "";

    /// <summary>启动中继;port 传 0 表示由系统分配。</summary>
    public bool Start(int port, string roomName)
    {
        if (Active)
            return false;
        try
        {
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            RoomName = string.IsNullOrWhiteSpace(roomName) ? "我的房间" : roomName.Trim();
            Active = true;
            _ = AcceptLoopAsync();
            System?.Invoke($"中继已启动 · 端口 {Port} · 房间 {RoomName}");
            return true;
        }
        catch (SocketException ex)
        {
            System?.Invoke("中继启动失败:" + ex.Message);
            return false;
        }
    }

    private async Task AcceptLoopAsync()
    {
        var listener = _listener!;
        try
        {
            while (Active)
            {
                var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                var session = new RelaySession(client, client.Client.RemoteEndPoint?.ToString() ?? "?");
                lock (_gate) _sessions.Add(session);
                _ = SessionLoopAsync(session);
            }
        }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private async Task SessionLoopAsync(RelaySession session)
    {
        var buffer = new byte[2048];
        try
        {
            var stream = session.Client.GetStream();
            while (Active)
            {
                var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0)
                    break;
                var text = Encoding.UTF8.GetString(buffer, 0, read);
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    Handle(session, line.TrimEnd('\r'));
            }
        }
        catch (Exception) { }
        finally
        {
            lock (_gate) _sessions.Remove(session);
            try { session.Client.Dispose(); } catch { }
            if (session.Name.Length > 0)
            {
                MemberLeft?.Invoke(session.Name);
                Broadcast("SYS " + session.Name + " 离开了房间");
            }
        }
    }

    private void Handle(RelaySession session, string line)
    {
        if (line.StartsWith("JOIN ", StringComparison.Ordinal))
        {
            session.Name = line[5..].Trim();
            if (session.Name.Length == 0)
                session.Name = "玩家";
            MemberJoined?.Invoke(session.Name);
            Broadcast("SYS " + session.Name + " 加入了房间");
            Message?.Invoke(session.Name + " 加入");
            return;
        }

        if (line.StartsWith("SAY ", StringComparison.Ordinal))
        {
            var body = line[4..];
            Broadcast($"MSG {session.Name} {body}");
            Message?.Invoke($"{session.Name}: {body}");
            return;
        }

        if (line == "PING")
            _ = SendAsync(session, "SYS PONG");
    }

    public void Broadcast(string payload)
    {
        RelaySession[] snapshot;
        lock (_gate) snapshot = [.. _sessions];
        foreach (var s in snapshot)
            _ = SendAsync(s, payload);
    }

    /// <summary>房主发言:广播给所有已连接客户端(同时进房主自己的消息流)。</summary>
    public void Say(string from, string text)
    {
        Broadcast($"MSG {from} {text}");
        Message?.Invoke($"{from}: {text}");
    }

    private static async Task SendAsync(RelaySession session, string payload)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(payload + "\n");
            await session.Client.GetStream().WriteAsync(bytes).ConfigureAwait(false);
        }
        catch (Exception) { }
    }

    public void Dispose()
    {
        Active = false;
        try { _listener?.Stop(); } catch { }
        RelaySession[] snapshot;
        lock (_gate) snapshot = [.. _sessions];
        foreach (var s in snapshot)
        {
            try { s.Client.Dispose(); } catch { }
        }
        lock (_gate) _sessions.Clear();
        System?.Invoke("中继已停止");
    }

    private sealed class RelaySession
    {
        public RelaySession(TcpClient client, string endpoint)
        {
            Client = client;
            Endpoint = endpoint;
        }

        public TcpClient Client { get; }
        public string Endpoint { get; }
        public string Name { get; set; } = "";
    }
}

/// <summary>中继客户端:可连本机自建中继、局域网中继或用户手动添加的中继地址。</summary>
public sealed class VoxLinkRelayClient : IDisposable
{
    private TcpClient? _client;

    public event Action<string>? Message;
    public event Action<string>? System;
    public event Action? Disconnected;

    public bool Connected => _client?.Connected == true;

    public async Task<bool> ConnectAsync(string host, int port, string myName,
        CancellationToken cancellation = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("中继地址不能为空", nameof(host));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "端口需在 1–65535 之间");

        _client = new TcpClient();
        await _client.ConnectAsync(host.Trim(), port, cancellation).ConfigureAwait(false);
        var join = Encoding.UTF8.GetBytes($"JOIN {myName}\n");
        await _client.GetStream().WriteAsync(join, cancellation).ConfigureAwait(false);
        _ = ReadLoopAsync();
        System?.Invoke($"已连接 {host}:{port}");
        return true;
    }

    public async Task SendAsync(string text)
    {
        if (_client is null || !_client.Connected)
            return;
        try
        {
            var bytes = Encoding.UTF8.GetBytes($"SAY {text}\n");
            await _client.GetStream().WriteAsync(bytes).ConfigureAwait(false);
        }
        catch (Exception) { }
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[2048];
        try
        {
            var stream = _client!.GetStream();
            while (true)
            {
                var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0)
                    break;
                var text = Encoding.UTF8.GetString(buffer, 0, read);
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    Dispatch(line.TrimEnd('\r'));
            }
        }
        catch (Exception) { }
        finally
        {
            Disconnected?.Invoke();
        }
    }

    private void Dispatch(string line)
    {
        if (line.StartsWith("MSGSYS", StringComparison.Ordinal))
            return;
        if (line.StartsWith("MSG ", StringComparison.Ordinal))
        {
            Message?.Invoke(line[4..]);
            return;
        }
        if (line.StartsWith("SYS ", StringComparison.Ordinal))
        {
            var body = line[4..];
            if (body != "PONG")
                System?.Invoke(body);
        }
    }

    public void Dispose()
    {
        try { _client?.Dispose(); } catch { }
        _client = null;
    }
}

/// <summary>局域网信标:中继主机周期性 UDP 广播自己,供同网段扫描。</summary>
public sealed class VoxLinkLanBeacon : IDisposable
{
    public const int BeaconPort = 27717;
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;

    public int RelayPort { get; private set; }
    public string RoomName { get; private set; } = "";

    public void Start(int relayPort, string roomName, string hostName)
    {
        RelayPort = relayPort;
        RoomName = roomName;
        _cts = new CancellationTokenSource();
        _udp = new UdpClient { EnableBroadcast = true };
        var payload = Encoding.UTF8.GetBytes($"SLRELAY|{relayPort}|{roomName}|{hostName}");
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await _udp.SendAsync(payload, new IPEndPoint(IPAddress.Broadcast, BeaconPort), token)
                        .ConfigureAwait(false);
                }
                catch (Exception) { }
                try { await Task.Delay(2000, token).ConfigureAwait(false); } catch { }
            }
        }, token);
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;
        try { _udp?.Dispose(); } catch { }
        _udp = null;
    }
}

/// <summary>局域网房间条目。</summary>
/// <param name="Address">中继主机 IP</param>
/// <param name="Port">中继端口</param>
/// <param name="Room">房间名</param>
/// <param name="HostName">广播者名字</param>
public sealed record LanRoom(string Address, int Port, string Room, string HostName)
{
    public string Key => $"{Address}:{Port}";
    public string Display => $"{Room} · {Address}:{Port}";
}

/// <summary>局域网扫描:监听 UDP 信标,聚合同网段可用房间。</summary>
public sealed class VoxLinkLanScanner : IDisposable
{
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;

    /// <summary>发现新房间(线程池线程触发,订阅方自行调度 UI)。</summary>
    public event Action<LanRoom>? Found;

    public void Start()
    {
        if (_udp is not null)
            return;
        _cts = new CancellationTokenSource();
        try
        {
            _udp = new UdpClient();
            _udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udp.Client.Bind(new IPEndPoint(IPAddress.Any, VoxLinkLanBeacon.BeaconPort));
            _udp.EnableBroadcast = true;
        }
        catch (SocketException)
        {
            // 端口被占用(例如本机同时是主机):保持静默,扫描不可用
            _udp = null;
            return;
        }

        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await _udp.ReceiveAsync(token).ConfigureAwait(false);
                    var text = Encoding.UTF8.GetString(result.Buffer);
                    var parts = text.Split('|');
                    if (parts.Length < 4 || parts[0] != "SLRELAY")
                        continue;
                    if (!int.TryParse(parts[1], out var port))
                        continue;
                    Found?.Invoke(new LanRoom(result.RemoteEndPoint.Address.ToString(), port, parts[2], parts[3]));
                }
                catch (OperationCanceledException) { break; }
                catch (Exception) { }
            }
        }, token);
    }

    public void Dispose()
    {
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;
        try { _udp?.Dispose(); } catch { }
        _udp = null;
    }
}