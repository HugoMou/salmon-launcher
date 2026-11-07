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
using SL.Core.Account;
using SL.Core.Logging;

namespace SL.Core.Migration;

/// <summary>Result of running an import from an old launcher.</summary>
public sealed record MigrationResult(
    bool Success,
    int AccountsImported,
    int InstancesImported,
    string Message);

/// <summary>Base class for concrete importers — one per supported launcher family.</summary>
public abstract class MigrationRunner
{
    public abstract LauncherKind Kind { get; }
    public abstract MigrationResult Import(MigrationSource source);

    protected static MigrationResult Ok(int accounts, int instances, string msg)
        => new(true, accounts, instances, msg);

    protected static MigrationResult Fail(string msg)
        => new(false, 0, 0, msg);

    /// <summary>Import a list of player names as offline accounts into SalmonLauncher.</summary>
    protected int ImportAsOffline(IEnumerable<string> names)
    {
        var repo = AccountStore.Load();
        var imported = 0;
        foreach (var name in names)
        {
            var clean = name.Trim();
            if (string.IsNullOrWhiteSpace(clean))
                continue;
            if (repo.Accounts.Any(a => a.Name.Equals(clean, StringComparison.OrdinalIgnoreCase)))
                continue;
            AccountStore.AddOffline(clean);
            imported++;
        }
        return imported;
    }
}

/// <summary>HMCL importer — reads hmcl.json account array and imports player names as offline accounts.</summary>
public sealed class HMCLMigrationRunner : MigrationRunner
{
    public override LauncherKind Kind => LauncherKind.HMCL;

    public override MigrationResult Import(MigrationSource source)
    {
        try
        {
            var hmclPath = Directory.Exists(source.RootPath)
                ? Path.Combine(source.RootPath, "hmcl.json")
                : source.RootPath;

            if (!File.Exists(hmclPath))
                return Fail($"找不到 HMCL 数据文件: {hmclPath}");

            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(hmclPath));
            var names = new List<string>();

            if (doc.RootElement.TryGetProperty("accounts", out var arr)
                && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var acct in arr.EnumerateArray())
                {
                    var name = acct.TryGetProperty("username", out var u) ? u.GetString()
                             : acct.TryGetProperty("name", out var n) ? n.GetString()
                             : null;
                    if (!string.IsNullOrWhiteSpace(name))
                        names.Add(name);
                }
            }

            var imported = ImportAsOffline(names);
            return Ok(imported, 0, imported > 0
                ? $"已导入 {imported} 个账户(离线模式),Microsoft 账户请在启动器内重新登录"
                : "未发现新账户可导入");
        }
        catch (Exception ex)
        {
            AppLog.Error("Migration.HMCL", ex);
            return Fail($"HMCL 导入失败: {ex.Message}");
        }
    }
}

/// <summary>Official Mojang launcher importer — reads launcher_accounts.json and imports player names.</summary>
public sealed class OfficialMigrationRunner : MigrationRunner
{
    public override LauncherKind Kind => LauncherKind.Official;

    public override MigrationResult Import(MigrationSource source)
    {
        try
        {
            var accountsFile = Path.Combine(source.RootPath, "launcher_accounts.json");
            var names = new List<string>();

            if (File.Exists(accountsFile))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(accountsFile));
                if (doc.RootElement.TryGetProperty("accounts", out var arr)
                    && arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var acct in arr.EnumerateArray())
                    {
                        var name = acct.TryGetProperty("minecraftProfile", out var mp)
                                   && mp.TryGetProperty("name", out var mn) ? mn.GetString()
                                   : acct.TryGetProperty("user", out var u)
                                   && u.TryGetProperty("displayName", out var dn) ? dn.GetString()
                                   : null;
                        if (!string.IsNullOrWhiteSpace(name))
                            names.Add(name);
                    }
                }
            }

            var imported = ImportAsOffline(names);
            return Ok(imported, 0, imported > 0
                ? $"已导入 {imported} 个账户,如需正版登录请在启动器内重新授权"
                : "未发现新账户可导入");
        }
        catch (Exception ex)
        {
            AppLog.Error("Migration.Official", ex);
            return Fail($"官方启动器导入失败: {ex.Message}");
        }
    }
}

/// <summary>MultiMC / Prism Launcher importer — reads accounts.json and imports player names.</summary>
public sealed class MultiMCMigrationRunner : MigrationRunner
{
    public override LauncherKind Kind => LauncherKind.MultiMC;

    public override MigrationResult Import(MigrationSource source)
    {
        try
        {
            var accountsFile = Path.Combine(source.RootPath, "accounts.json");
            var names = new List<string>();

            if (File.Exists(accountsFile))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(accountsFile));

                System.Text.Json.JsonElement arr;
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                    arr = doc.RootElement;
                else if (!doc.RootElement.TryGetProperty("accounts", out arr))
                    arr = default;

                if (arr.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var acct in arr.EnumerateArray())
                    {
                        var name = acct.TryGetProperty("profile", out var p)
                                   && p.TryGetProperty("name", out var pn) ? pn.GetString()
                                   : acct.TryGetProperty("username", out var u) ? u.GetString()
                                   : null;
                        if (!string.IsNullOrWhiteSpace(name))
                            names.Add(name);
                    }
                }
            }

            var imported = ImportAsOffline(names);
            return Ok(imported, 0, imported > 0
                ? $"已导入 {imported} 个账户"
                : "未发现新账户可导入");
        }
        catch (Exception ex)
        {
            AppLog.Error("Migration.MultiMC", ex);
            return Fail($"MultiMC 导入失败: {ex.Message}");
        }
    }
}

/// <summary>Generic Minecraft importer — no launcher-specific data to import, just confirms the data dir.</summary>
public sealed class GenericMinecraftMigrationRunner : MigrationRunner
{
    public override LauncherKind Kind => LauncherKind.GenericMinecraft;

    public override MigrationResult Import(MigrationSource source)
        => Ok(0, 0, "已识别 Minecraft 数据目录;版本列表与存档会在启动器使用中自动发现");
}

/// <summary>Registry that maps a source's LauncherKind to the right runner.</summary>
public static class MigrationRunnerRegistry
{
    private static readonly Dictionary<LauncherKind, MigrationRunner> Runners = new()
    {
        [LauncherKind.HMCL] = new HMCLMigrationRunner(),
        [LauncherKind.Official] = new OfficialMigrationRunner(),
        [LauncherKind.MultiMC] = new MultiMCMigrationRunner(),
        [LauncherKind.GenericMinecraft] = new GenericMinecraftMigrationRunner(),
    };

    public static MigrationRunner Get(LauncherKind kind) => Runners[kind];
}
