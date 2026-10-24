using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SL.Core.Net;

/// <summary>房间成员信息。</summary>
/// <param name="Name"></param>
/// <param name="Endpoint">回环端点(演示通道)</param>
public sealed record VoxLinkMember(string Name, string Endpoint);

/// <summary>
/// 阶段六:VoxLink 本地信令演示通路(TCP 回环)。
/// 真实公网穿透需要中继服务器,阶段七接出;当前实现保证"创建房间/邀请码/加入/消息"全链路真实工作(本机)。
/// </summary>
public sealed class VoxLinkHost : IDisposable
{
    private TcpListener? _listener;
    private readonly List<TcpClient> _clients = [];
    private readonly object _gate = new();

    /// <summary>成员变动 / 收到消息(线程池线程触发,订阅方自行调度 UI)。</summary>
    public event Action<VoxLinkMember>? Joined;
    public event Action<string>? Message;
    public event Action? Stopped;

    public int Port { get; private set; }
    public string InviteCode { get; private set; } = "";
    public bool Active { get; private set; }

    public void Start(string roomName)
    {
        if (Active)
            return;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        // 邀请码 = SLROOM-<端口 36 进制大写>-<房间名长度 16 进制>(本地演示)
        // 注:Convert.ToString(v,36) 不支持 36 进制(仅 2/8/10/16),这里用自有编码
        InviteCode = $"SLROOM-{ToBase36(Port)}-{roomName.Length:X2}";
        Active = true;
        _ = AcceptLoopAsync();
    }

    private async Task AcceptLoopAsync()
    {
        var listener = _listener!;
        try
        {
            while (Active)
            {
                var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                lock (_gate) _clients.Add(client);
                Joined?.Invoke(new VoxLinkMember(client.Client.RemoteEndPoint?.ToString() ?? "?", "localhost:" + Port));
                _ = ReadLoopAsync(client);
            }
        }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private async Task ReadLoopAsync(TcpClient client)
    {
        var buffer = new byte[512];
        try
        {
            var stream = client.GetStream();
            while (Active)
            {
                var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0)
                    break;
                var text = Encoding.UTF8.GetString(buffer, 0, read).TrimEnd('\0', '\n');
                Message?.Invoke(text + "(来自 " + client.Client.RemoteEndPoint + ")");
            }
        }
        catch (Exception) { }
        finally
        {
            lock (_gate) _clients.Remove(client);
        }
    }

    /// <summary>向所有已连接成员广播。</summary>
    public async void Broadcast(string message)
    {
        if (!Active)
            return;
        var bytes = Encoding.UTF8.GetBytes(message + "\n");
        TcpClient[] snapshot;
        lock (_gate) snapshot = [.. _clients];
        foreach (var c in snapshot)
        {
            try
            {
                await c.GetStream().WriteAsync(bytes).ConfigureAwait(false);
            }
            catch (Exception) { }
        }
    }

    public static string ToBase36(int value)
    {
        if (value == 0)
            return "0";
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var sb = new StringBuilder();
        while (value > 0)
        {
            sb.Insert(0, digits[value % 36]);
            value /= 36;
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        Active = false;
        try { _listener?.Stop(); } catch { }
        List<TcpClient> snapshot;
        lock (_gate) snapshot = [.. _clients];
        foreach (var c in snapshot)
        {
            try { c.Dispose(); } catch { }
        }
        _clients.Clear();
        Stopped?.Invoke();
    }
}

/// <summary>加入房间(本地信令演示):解析邀请码 → 连接回环端口 → 发送"加入"消息。</summary>
public sealed class VoxLinkGuest : IDisposable
{
    private TcpClient? _client;

    /// <summary>收到房主消息。</summary>
    public event Action<string>? Message;

    public async Task<bool> ConnectAsync(string inviteCode, string myName, CancellationToken cancellation = default)
    {
        // SLROOM-<port36>-<len16>
        var parts = inviteCode.Split('-');
        if (parts.Length != 3 || !parts[0].Equals("SLROOM", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("邀请码格式不正确(SLROOM-xxxx-xx)");
        if (!TryParseBase36(parts[1], out var port) || port is < 1024 or > 65535)
            throw new FormatException("邀请码端口非法");

        _client = new TcpClient();
        await _client.ConnectAsync(IPAddress.Loopback, port, cancellation).ConfigureAwait(false);
        var bytes = Encoding.UTF8.GetBytes($"JOIN {myName}\n");
        await _client.GetStream().WriteAsync(bytes, cancellation).ConfigureAwait(false);
        _ = ReadLoopAsync();
        return true;
    }

    public async void Send(string message)
    {
        if (_client is null || !_client.Connected)
            return;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(message + "\n");
            await _client!.GetStream().WriteAsync(bytes).ConfigureAwait(false);
        }
        catch (Exception) { }
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[512];
        try
        {
            var stream = _client!.GetStream();
            while (true)
            {
                var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0)
                    break;
                Message?.Invoke(Encoding.UTF8.GetString(buffer, 0, read).TrimEnd('\0', '\n'));
            }
        }
        catch (Exception) { }
    }

    private static bool TryParseBase36(string s, out int value)
    {
        value = 0;
        foreach (var ch in s.ToUpperInvariant())
        {
            int digit = ch switch
            {
                >= '0' and <= '9' => ch - '0',
                >= 'A' and <= 'Z' => ch - 'A' + 10,
                _ => -1,
            };
            if (digit < 0)
                return false;
            value = value * 36 + digit;
        }
        return true;
    }

    public void Dispose()
    {
        try { _client?.Dispose(); } catch { }
    }
}
