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
using System.Text.Json;
using SL.Core.Logging;

namespace SL.Core.Migration;

/// <summary>Old launcher families we can import from.</summary>
public enum LauncherKind
{
    /// <summary>Official Minecraft Launcher (Mojang) — launcher_profiles.json in .minecraft</summary>
    Official,
    /// <summary>Hello Minecraft! Launcher — .hmcl directory in AppData or portable</summary>
    HMCL,
    /// <summary>MultiMC / Prism Launcher — instances directory</summary>
    MultiMC,
    /// <summary>Plain Minecraft directory — .minecraft with profiles and saves but no known launcher wrapper</summary>
    GenericMinecraft,
}

/// <summary>A detected importable source on disk.</summary>
public sealed record MigrationSource(
    LauncherKind Kind,
    string DisplayName,
    string RootPath,
    int AccountCount,
    int InstanceCount,
    int GameVersionCount,
    bool CanImport);

/// <summary>Scans known locations for old launcher data so the welcome page can offer a one-click import.</summary>
public static class MigrationDetector
{
    public static IReadOnlyList<MigrationSource> ScanAll()
    {
        var results = new List<MigrationSource>();

        try
        {
            foreach (var candidate in Candidates())
            {
                if (candidate is not null)
                    results.Add(candidate);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("Migration", ex);
        }

        return results;
    }

    private static IEnumerable<MigrationSource?> Candidates()
    {
        // Minecraft directory (shared by HMCL, official launcher, and many others)
        var mc = MinecraftDir();

        // 1. HMCL — stores accounts.json and version list inside .minecraft/hmcl.json or .hmcl/
        if (mc is not null)
        {
            // HMCL v3: hmcl.json inside .minecraft
            var hmclJson = Path.Combine(mc, "hmcl.json");
            if (File.Exists(hmclJson))
                yield return DetectHMCL(mc, hmclJson);

            // HMCL portable: .hmcl sibling or child
            var hmclDir = Path.Combine(mc, ".hmcl");
            if (!Directory.Exists(hmclDir))
            {
                var parent = Path.GetDirectoryName(mc);
                if (!string.IsNullOrEmpty(parent))
                    hmclDir = Path.Combine(parent, ".hmcl");
            }
            if (Directory.Exists(hmclDir))
                yield return DetectHMCL(mc, hmclDir);

            // 2. Official launcher (Mojang) — launcher_profiles.json
            var profiles = Path.Combine(mc, "launcher_profiles.json");
            if (File.Exists(profiles))
                yield return DetectOfficial(mc, profiles);

            // 3. Generic Minecraft — has versions folder (launcher-agnostic)
            var versions = Path.Combine(mc, "versions");
            if (Directory.Exists(versions) && !File.Exists(hmclJson) && !File.Exists(profiles))
                yield return DetectGeneric(mc, versions);
        }

        // 4. MultiMC / Prism — standalone instances directory
        foreach (var mmcRoot in MultiMCRoots())
        {
            if (Directory.Exists(mmcRoot))
                yield return DetectMultiMC(mmcRoot);
        }
    }

    private static string? MinecraftDir()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
            return null;
        var mc = Path.Combine(appData, ".minecraft");
        return Directory.Exists(mc) ? mc : null;
    }

    private static IEnumerable<string> MultiMCRoots()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(appData))
            yield return Path.Combine(appData, "MultiMC");
        if (!string.IsNullOrEmpty(localAppData))
        {
            yield return Path.Combine(localAppData, "MultiMC");
            yield return Path.Combine(localAppData, "PrismLauncher");
        }
    }

    // ---------- Individual detectors ----------

    private static MigrationSource? DetectHMCL(string mcRoot, string hmclPath)
    {
        try
        {
            int accounts = 0;
            if (File.Exists(hmclPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(hmclPath));
                if (doc.RootElement.TryGetProperty("accounts", out var arr))
                    accounts = arr.ValueKind == JsonValueKind.Array ? arr.GetArrayLength() : 0;
            }

            int versions = CountSubdirs(Path.Combine(mcRoot, "versions"));
            int instances = CountSubdirs(Path.Combine(mcRoot, "instances"));

            return new MigrationSource(
                LauncherKind.HMCL,
                "Hello Minecraft! 启动器",
                hmclPath,
                accounts,
                Math.Max(instances, 1), // HMCL has an implicit default instance
                versions,
                true);
        }
        catch
        {
            return null;
        }
    }

    private static MigrationSource? DetectOfficial(string mcRoot, string profilesPath)
    {
        try
        {
            int profiles = 0;
            int accounts = 0;
            using (var doc = JsonDocument.Parse(File.ReadAllText(profilesPath)))
            {
                if (doc.RootElement.TryGetProperty("profiles", out var pObj))
                    profiles = pObj.ValueKind == JsonValueKind.Object ? pObj.EnumerateObject().Count() : 0;
            }

            // Official launcher stores accounts in a separate file on newer versions
            var accountsFile = Path.Combine(mcRoot, "launcher_accounts.json");
            if (File.Exists(accountsFile))
            {
                try
                {
                    using var doc2 = JsonDocument.Parse(File.ReadAllText(accountsFile));
                    if (doc2.RootElement.TryGetProperty("accounts", out var arr))
                        accounts = arr.ValueKind == JsonValueKind.Array ? arr.GetArrayLength() : 0;
                    else
                        accounts = profiles; // fall back: each profile ≈ one account on legacy setups
                }
                catch
                {
                    accounts = profiles;
                }
            }
            else
            {
                accounts = profiles;
            }

            int versions = CountSubdirs(Path.Combine(mcRoot, "versions"));

            return new MigrationSource(
                LauncherKind.Official,
                "Minecraft 官方启动器",
                mcRoot,
                accounts,
                profiles,
                versions,
                true);
        }
        catch
        {
            return null;
        }
    }

    private static MigrationSource? DetectMultiMC(string root)
    {
        try
        {
            int instances = CountSubdirs(Path.Combine(root, "instances"));
            int accounts = 0;

            var accountsFile = Path.Combine(root, "accounts.json");
            if (File.Exists(accountsFile))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(accountsFile));
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        accounts = doc.RootElement.GetArrayLength();
                    else if (doc.RootElement.TryGetProperty("accounts", out var arr))
                        accounts = arr.ValueKind == JsonValueKind.Array ? arr.GetArrayLength() : 0;
                }
                catch { }
            }

            return new MigrationSource(
                LauncherKind.MultiMC,
                Directory.Exists(Path.Combine(root, "PrismLauncher.exe")) ? "Prism Launcher" : "MultiMC",
                root,
                accounts,
                instances,
                0, // MultiMC instances bundle their own version info
                true);
        }
        catch
        {
            return null;
        }
    }

    private static MigrationSource? DetectGeneric(string mcRoot, string versionsDir)
    {
        try
        {
            int versions = CountSubdirs(versionsDir);
            int saves = CountSubdirs(Path.Combine(mcRoot, "saves"));

            if (versions == 0 && saves == 0)
                return null;

            return new MigrationSource(
                LauncherKind.GenericMinecraft,
                "Minecraft 存档与版本",
                mcRoot,
                0,
                saves,
                versions,
                true);
        }
        catch
        {
            return null;
        }
    }

    private static int CountSubdirs(string path)
    {
        if (!Directory.Exists(path))
            return 0;
        try
        {
            return Directory.GetDirectories(path).Length;
        }
        catch
        {
            return 0;
        }
    }
}
