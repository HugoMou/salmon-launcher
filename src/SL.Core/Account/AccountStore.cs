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

namespace SL.Core.Account;

/// <summary>One account record: an offline account has only a name; a premium account embeds the full Microsoft session.</summary>
public sealed record AccountEntry
{
    /// <summary>Stable Id: "msa:&lt;uuid&gt;" or "offline:&lt;player name&gt;".</summary>
    public string Id { get; init; } = "";
    /// <summary>"offline" (offline account) or "msa" (Microsoft premium).</summary>
    public string Kind { get; init; } = "offline";
    public string Name { get; init; } = "";
    /// <summary>Full session of a premium account (includes refresh_token); null for offline.</summary>
    public MsSession? Session { get; init; }
    public DateTimeOffset SavedAt { get; init; } = DateTimeOffset.Now;
}

/// <summary>On-disk shape of accounts\accounts.json: the account collection plus the active account Id.</summary>
public sealed record AccountRepository
{
    public List<AccountEntry> Accounts { get; init; } = [];
    public string ActiveId { get; init; } = "";
}

/// <summary>
/// Multi-account store: premium (multiple Microsoft accounts) and offline accounts stored together, with add/update/remove/switch.
/// The legacy single-session msa.json is migrated automatically into one premium account on first read.
/// </summary>
public static class AccountStore
{
    private static readonly object Gate = new();

    public static AccountRepository Load()
    {
        lock (Gate)
        {
            try
            {
                var path = RepositoryPath();
                if (!File.Exists(path))
                {
                    // Migration: the legacy build only had the single-session accounts\msa.json; turn it into the first premium account of the store
                    var legacy = MsAuth.LoadSession();
                    if (legacy is { Uuid.Length: > 0 })
                    {
                        var migrated = new AccountRepository
                        {
                            Accounts =
                            [
                                new AccountEntry
                                {
                                    Id = "msa:" + legacy.Uuid,
                                    Kind = "msa",
                                    Name = legacy.Name,
                                    Session = legacy,
                                },
                            ],
                            ActiveId = "msa:" + legacy.Uuid,
                        };
                        SaveCore(migrated);
                        return migrated;
                    }
                    return new AccountRepository();
                }
                return JsonSerializer.Deserialize<AccountRepository>(File.ReadAllText(path)) ?? new AccountRepository();
            }
            catch (Exception)
            {
                return new AccountRepository();
            }
        }
    }

    /// <summary>Add a premium account or update one by Id, then make it active.</summary>
    public static AccountEntry UpsertMsa(MsSession session)
    {
        lock (Gate)
        {
            var repo = Load();
            var id = "msa:" + session.Uuid;
            var entry = new AccountEntry
            {
                Id = id,
                Kind = "msa",
                Name = session.Name,
                Session = session,
                SavedAt = DateTimeOffset.Now,
            };
            repo.Accounts.RemoveAll(a => a.Id == id);
            repo.Accounts.Insert(0, entry);
            SaveCore(repo with { ActiveId = id });
            return entry;
        }
    }

    /// <summary>Add (or select an existing) offline account and make it active.</summary>
    public static AccountEntry AddOffline(string name)
    {
        lock (Gate)
        {
            var clean = name.Trim();
            var repo = Load();
            var id = "offline:" + clean;
            var exists = repo.Accounts.FirstOrDefault(a => a.Id == id);
            if (exists is not null)
            {
                SaveCore(repo with { ActiveId = id });
                return exists;
            }
            var entry = new AccountEntry { Id = id, Kind = "offline", Name = clean };
            repo.Accounts.Add(entry);
            SaveCore(repo with { ActiveId = id });
            return entry;
        }
    }

    /// <summary>Delete an account; if the active account is deleted, the active slot moves to the first remaining account (may be empty).</summary>
    public static void Remove(string id)
    {
        lock (Gate)
        {
            var repo = Load();
            repo.Accounts.RemoveAll(a => a.Id == id);
            var activeId = repo.ActiveId == id ? repo.Accounts.FirstOrDefault()?.Id ?? "" : repo.ActiveId;
            SaveCore(repo with { ActiveId = activeId });
        }
    }

    public static void SetActive(string id)
    {
        lock (Gate)
        {
            var repo = Load();
            if (repo.Accounts.All(a => a.Id != id))
                return;
            SaveCore(repo with { ActiveId = id });
        }
    }

    /// <summary>Current active account; null when the store is empty (callers fall back to the offline name).</summary>
    public static AccountEntry? Active()
    {
        var repo = Load();
        return repo.Accounts.FirstOrDefault(a => a.Id == repo.ActiveId);
    }

    public static string RepositoryPath() => Path.Combine(Boot.Runtime.Root, "accounts", "accounts.json");

    private static void SaveCore(AccountRepository repo)
    {
        var path = RepositoryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(repo, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, path, overwrite: true);
    }
}
