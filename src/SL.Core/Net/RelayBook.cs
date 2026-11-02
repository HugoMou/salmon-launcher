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
using System.Text.Json;

namespace SL.Core.Net;

/// <summary>A relay server added manually by the user.</summary>
/// <param name="Name">Display name</param>
/// <param name="Host">Address (IP or domain name)</param>
/// <param name="Port">Port</param>
public sealed record RelayEndpoint(string Name, string Host, int Port)
{
    public string Key => $"{Host}:{Port}";
    public string Display => $"{Name} · {Host}:{Port}";
}

/// <summary>Relay server favorites: stored as relays.json in the data directory and reused by "Add relay server".</summary>
public static class RelayBook
{
    private static string FilePath => Path.Combine(Boot.Runtime.Root, "relays.json");

    public static IReadOnlyList<RelayEndpoint> List()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            var json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize<List<RelayEndpoint>>(json);
            return list ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static void Add(RelayEndpoint endpoint)
    {
        var list = List().ToList();
        list.RemoveAll(e => e.Key == endpoint.Key);
        list.Add(endpoint);
        Save(list);
    }

    public static void Remove(string key)
    {
        var list = List().Where(e => e.Key != key).ToList();
        Save(list);
    }

    private static void Save(List<RelayEndpoint> list)
    {
        try
        {
            Directory.CreateDirectory(Boot.Runtime.Root);
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch (Exception)
        {
            // a corrupted favorites file does not affect the main multiplayer flow
        }
    }
}