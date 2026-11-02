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
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SL.Core.Net;

/// <summary>
/// TCP port forwarding — the actual carrier of the multiplayer feature:
/// listen on one port on this machine and forward every inbound connection bidirectionally to the target host:port.
/// "Self-hosted relay" forwards to a local server; "Add relay server" forwards to a remote relay address.
/// Only bytes are moved; the game protocol is never parsed.
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

    /// <summary>Start listening; listenPort 0 lets the system assign a port. The failure reason is written to error.</summary>
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

            // local target + same port = a self-loop that would replicate connections forever, so reject it outright
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
            // once one direction ends, half-close the peer so the other side can wind down naturally
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

/// <summary>Worlds opened to LAN by a player (from Minecraft multicast announcements).</summary>
/// <param name="Address">Host IP</param>
/// <param name="Port">Room port</param>
/// <param name="Motd">Save/room name</param>
public sealed record McLanWorld(string Address, int Port, string Motd)
{
    public string Key => $"{Address}:{Port}";
    public string Display => $"{Motd} · {Address}:{Port}";
}

/// <summary>
/// Minecraft native LAN discovery: listen for multicast announcements on 224.0.2.60:4445
/// (<c>[MOTD]name[/MOTD][AD]port[/AD]</c>), i.e. what the game broadcasts after "Open to LAN".
/// </summary>
public sealed class McLanScanner : IDisposable
{
    private static readonly IPAddress Group = IPAddress.Parse("224.0.2.60");
    private const int ListenPort = 4445;

    private UdpClient? _udp;
    private CancellationTokenSource? _cts;

    /// <summary>Discover rooms (fires on thread-pool threads; subscribers marshal to the UI themselves).</summary>
    public event Action<McLanWorld>? Found;

    /// <summary>Reason callback when scanning is unavailable (port already in use, etc.).</summary>
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
                    // one bad packet does not affect subsequent listening
                }
            }
        }, token);
    }

    /// <summary>Parse an MC multicast announcement, format: [MOTD]name[/MOTD][AD]port[/AD]</summary>
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
