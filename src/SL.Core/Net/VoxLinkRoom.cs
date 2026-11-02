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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace SL.Core.Net;

/// <summary>Room member information.</summary>
/// <param name="Name"></param>
/// <param name="Endpoint">Loopback endpoint (demo channel)</param>
public sealed record VoxLinkMember(string Name, string Endpoint);

/// <summary>
/// Stage 6: VoxLink local signaling demo path (TCP loopback).
/// Real public-network traversal needs a relay server and will be added in stage 7; the current implementation makes the whole "create room / invite code / join / message" chain work for real (on this machine).
/// </summary>
public sealed class VoxLinkHost : IDisposable
{
    private TcpListener? _listener;
    private readonly List<TcpClient> _clients = [];
    private readonly object _gate = new();

    /// <summary>Member change / message received (fires on thread-pool threads; subscribers marshal to the UI themselves).</summary>
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
        // invite code = SLROOM-<port in uppercase base 36>-<room name length in hex> (local demo)
        // note: Convert.ToString(v,36) does not support base 36 (only 2/8/10/16), so a custom encoding is used here
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

    /// <summary>Broadcast to every connected member.</summary>
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

/// <summary>Join a room (local signaling demo): parse the invite code → connect to the loopback port → send the "join" message.</summary>
public sealed class VoxLinkGuest : IDisposable
{
    private TcpClient? _client;

    /// <summary>A message from the host was received.</summary>
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
