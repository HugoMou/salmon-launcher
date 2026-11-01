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
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SL.Core.Net;

/// <summary>
/// TCP 端口转发 —— 联机功能的实际承载:
/// 在本机监听一个端口,把每条外部连接双向转发到目标 host:port。
/// 「自建中继」转发到本机服务器,「添加中继服务器」转发到远端中继地址。
/// 只搬运字节流,不解析游戏协议。
/// </summary>
public sealed class TcpForwarder : IDisposable
{
    private readonly List<Connection> _connections = [];
    private readonly object _gate = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private string _targetHost = "";
    private int _targetPort;

    private long _upBytes;
    private long _downBytes;

    public event Action<string>? Log;
    public event Action? StatsChanged;

    public int ListenPort { get; private set; }
    public bool Active { get; private set; }
    public string Target => $"{_targetHost}:{_targetPort}";

    public int ConnectionCount
    {
        get { lock (_gate) return _connections.Count; }
    }

    public long UpBytes => Interlocked.Read(ref _upBytes);
    public long DownBytes => Interlocked.Read(ref _downBytes);

    /// <summary>启动监听;listenPort 传 0 由系统分配。失败原因写入 error。</summary>
    public bool Start(int listenPort, string targetHost, int targetPort, out string error)
    {
        error = "";
        if (Active)
        {
            error = "转发已在运行";
            return false;
        }
        if (string.IsNullOrWhiteSpace(targetHost) || targetPort is < 1 or > 65535)
        {
            error = "目标地址或端口无效";
            return false;
        }

        try
        {
            _targetHost = targetHost.Trim();
            _targetPort = targetPort;
            _listener = new TcpListener(IPAddress.Any, listenPort);
            _listener.Start();
            ListenPort = ((IPEndPoint)_listener.LocalEndpoint).Port;

            // 本机目标 + 同端口 = 自我回环,会把连接无限复制下去,直接拒绝
            if (IsLoopback(_targetHost) && ListenPort == _targetPort)
            {
                _listener.Stop();
                _listener = null;
                error = $"本地端口 {ListenPort} 与目标端口相同会形成回环,请换一个本地端口";
                return false;
            }

            _cts = new CancellationTokenSource();
            Active = true;
            _ = AcceptLoopAsync(_cts.Token);
            Log?.Invoke($"已监听 0.0.0.0:{ListenPort} → {Target}");
            return true;
        }
        catch (SocketException ex)
        {
            error = listenPort == 0 ? ex.Message : $"端口 {listenPort} 不可用({ex.Message})";
            _listener = null;
            return false;
        }
    }

    private static bool IsLoopback(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
           || host is "::1" or "[::1]"
           || (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        var listener = _listener!;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var inbound = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
                _ = HandleAsync(inbound, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (SocketException) { }
    }

    private async Task HandleAsync(TcpClient inbound, CancellationToken token)
    {
        var peer = inbound.Client.RemoteEndPoint?.ToString() ?? "未知来源";
        var outbound = new TcpClient();
        try
        {
            await outbound.ConnectAsync(_targetHost, _targetPort, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log?.Invoke($"{peer} 连不上目标 {Target}:{ex.Message}");
            inbound.Dispose();
            outbound.Dispose();
            return;
        }

        var connection = new Connection(inbound, outbound);
        lock (_gate) _connections.Add(connection);
        Log?.Invoke($"{peer} 已接入(在线 {ConnectionCount})");
        StatsChanged?.Invoke();

        try
        {
            await Task.WhenAll(
                PumpAsync(inbound.GetStream(), outbound.GetStream(), up: true, token),
                PumpAsync(outbound.GetStream(), inbound.GetStream(), up: false, token)).ConfigureAwait(false);
        }
        catch (Exception) { }
        finally
        {
            lock (_gate) _connections.Remove(connection);
            try { inbound.Dispose(); } catch { }
            try { outbound.Dispose(); } catch { }
            Log?.Invoke($"{peer} 已断开(在线 {ConnectionCount})");
            StatsChanged?.Invoke();
        }
    }

    private async Task PumpAsync(NetworkStream from, NetworkStream to, bool up, CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!token.IsCancellationRequested)
            {
                var read = await from.ReadAsync(buffer, token).ConfigureAwait(false);
                if (read <= 0)
                    break;
                await to.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                if (up)
                    Interlocked.Add(ref _upBytes, read);
                else
                    Interlocked.Add(ref _downBytes, read);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
        finally
        {
            // 单向结束后通知对端半关闭,让另一方自然收尾
            try { to.Socket.Shutdown(SocketShutdown.Send); } catch { }
        }
    }

    public void Dispose()
    {
        Active = false;
        try { _cts?.Cancel(); } catch { }
        _cts?.Dispose();
        _cts = null;
        try { _listener?.Stop(); } catch { }
        _listener = null;

        Connection[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _connections];
            _connections.Clear();
        }
        foreach (var connection in snapshot)
        {
            try { connection.Inbound.Dispose(); } catch { }
            try { connection.Outbound.Dispose(); } catch { }
        }
        Log?.Invoke("转发已停止");
        StatsChanged?.Invoke();
    }

    private sealed record Connection(TcpClient Inbound, TcpClient Outbound);
}

/// <summary>局域网里被玩家「对局域网开放」的存档(来自 Minecraft 的组播公告)。</summary>
/// <param name="Address">房主 IP</param>
/// <param name="Port">房间端口</param>
/// <param name="Motd">存档/房间名</param>
public sealed record McLanWorld(string Address, int Port, string Motd)
{
    public string Key => $"{Address}:{Port}";
    public string Display => $"{Motd} · {Address}:{Port}";
}

/// <summary>
/// Minecraft 原生局域网发现:监听 224.0.2.60:4445 的组播公告
/// (<c>[MOTD]名称[/MOTD][AD]端口[/AD]</c>),即游戏「对局域网开放」后广播的内容。
/// </summary>
public sealed class McLanScanner : IDisposable
{
    private static readonly IPAddress Group = IPAddress.Parse("224.0.2.60");
    private const int ListenPort = 4445;

    private UdpClient? _udp;
    private CancellationTokenSource? _cts;

    /// <summary>发现房间(线程池线程触发,订阅方自行调度 UI)。</summary>
    public event Action<McLanWorld>? Found;

    /// <summary>扫描不可用(端口被占用等)时回调原因。</summary>
    public event Action<string>? Failed;

    public bool Running => _udp is not null;

    public void Start()
    {
        if (_udp is not null)
            return;

        _cts = new CancellationTokenSource();
        UdpClient udp;
        try
        {
            udp = new UdpClient();
            udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udp.Client.Bind(new IPEndPoint(IPAddress.Any, ListenPort));
            try { udp.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(Group, IPAddress.Any)); }
            catch (SocketException) { udp.JoinMulticastGroup(Group); }
        }
        catch (Exception ex)
        {
            Failed?.Invoke("局域网监听失败:" + ex.Message);
            _cts.Dispose();
            _cts = null;
            return;
        }

        _udp = udp;
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var result = await udp.ReceiveAsync(token).ConfigureAwait(false);
                    var world = Parse(Encoding.UTF8.GetString(result.Buffer), result.RemoteEndPoint.Address);
                    if (world is not null)
                        Found?.Invoke(world);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception)
                {
                    // 单条坏包不影响后续监听
                }
            }
        }, token);
    }

    /// <summary>解析 MC 组播公告,格式: [MOTD]名称[/MOTD][AD]端口[/AD]</summary>
    public static McLanWorld? Parse(string payload, IPAddress source)
    {
        var motd = Between(payload, "[MOTD]", "[/MOTD]");
        var ad = Between(payload, "[AD]", "[/AD]");
        if (ad is null || !int.TryParse(ad, out var port) || port is < 1 or > 65535)
            return null;
        return new McLanWorld(source.ToString(), port, string.IsNullOrWhiteSpace(motd) ? "Minecraft 局域网世界" : motd);
    }

    private static string? Between(string text, string open, string close)
    {
        var start = text.IndexOf(open, StringComparison.Ordinal);
        if (start < 0)
            return null;
        start += open.Length;
        var end = text.IndexOf(close, start, StringComparison.Ordinal);
        return end < 0 ? null : text[start..end];
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
