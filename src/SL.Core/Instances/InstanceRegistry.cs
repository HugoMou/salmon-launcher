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

namespace SL.Core.Instances;

/// <summary>An installed instance (disk fact, not demo data).</summary>
/// <param name="InstanceId">Directory name = version ID</param>
/// <param name="InstalledAt">When the core jar was written to disk</param>
/// <param name="SizeMb">Approximate size of the instance directory</param>
public sealed record InstanceInfo(string InstanceId, DateTimeOffset InstalledAt, long SizeMb);

/// <summary>Mods inside an instance (.jar / .disabled under the mods directory).</summary>
/// <param name="FileName">Actual file name (including the .disabled suffix)</param>
/// <param name="BaseName">Display name with .disabled removed</param>
/// <param name="SizeBytes"></param>
/// <param name="Enabled">Enabled means the .disabled suffix is absent</param>
/// <param name="ModifiedAt"></param>
public sealed record ModInfo(string FileName, string BaseName, long SizeBytes, bool Enabled, DateTimeOffset ModifiedAt);

/// <summary>
/// Stage 4: instance registry. The three-tier directory layout established in stage 2 is the source of truth;
/// mod enable/disable (renaming the .disabled suffix) / delete / import all converge in this layer, and Pres only calls it.
/// </summary>
public static class InstanceRegistry
{
    public static IReadOnlyList<InstanceInfo> List()
    {
        var root = Boot.Runtime.InstancesDir;
        var list = new List<InstanceInfo>();
        if (!Directory.Exists(root))
            return list;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var id = Path.GetFileName(dir);
            var versionJar = Path.Combine(dir, "versions", id + ".jar");
            if (!File.Exists(versionJar))
                continue;

            var size = 0L;
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    size += new FileInfo(f).Length;
            }
            catch (IOException) { }

            list.Add(new InstanceInfo(id, new DateTimeOffset(File.GetLastWriteTimeUtc(versionJar)), size / (1024 * 1024)));
        }

        return list.OrderByDescending(i => i.InstalledAt).ToList();
    }

    /// <summary>Most recently installed instance (default target of the launch pipeline).</summary>
    public static InstanceInfo? Latest()
    {
        var all = List();
        return all.Count > 0 ? all[0] : null;
    }

    // ------------------------------------------------------------ Mods

    public static string ModsDirOf(string instanceId)
        => Path.Combine(Boot.Runtime.InstancesDir, instanceId, "mods");

    public static IReadOnlyList<ModInfo> ListMods(string instanceId)
    {
        var dir = ModsDirOf(instanceId);
        var list = new List<ModInfo>();
        if (!Directory.Exists(dir))
            return list;

        foreach (var path in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is not (".jar" or ".disabled"))
                continue;
            var name = Path.GetFileName(path);
            var info = new FileInfo(path);
            var enabled = ext == ".jar";
            list.Add(new ModInfo(name, enabled ? name : name[..^".disabled".Length],
                info.Length, enabled, new DateTimeOffset(info.LastWriteTime)));
        }

        return list.OrderByDescending(m => m.Enabled).ThenByDescending(m => m.ModifiedAt).ToList();
    }

    /// <summary>Toggle enable/disable: append or strip the .disabled suffix (the file system is the state). Returns the enabled state after toggling.</summary>
    public static bool ToggleMod(string instanceId, string fileName)
    {
        var dir = ModsDirOf(instanceId);
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path))
            throw new FileNotFoundException(fileName);

        if (fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
        {
            var renamed = path[..^".disabled".Length];
            if (File.Exists(renamed))
                throw new IOException($"目标已存在:{Path.GetFileName(renamed)}");
            Directory.Move(path, renamed);
            return true; // enabled as soon as the rename succeeds
        }

        var disabled = path + ".disabled";
        if (File.Exists(disabled))
            throw new IOException($"目标已存在:{Path.GetFileName(disabled)}");
        Directory.Move(path, disabled);
        return false; // disable succeeded
    }

    public static void DeleteMod(string instanceId, string fileName)
    {
        var path = Path.Combine(ModsDirOf(instanceId), fileName);
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>Import from a local file (copy, never move; on a name clash, rename with a sequence number before overwriting).</summary>
    public static string ImportMod(string instanceId, string sourcePath)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException(sourcePath);
        var dir = ModsDirOf(instanceId);
        Directory.CreateDirectory(dir);

        var name = Path.GetFileName(sourcePath);
        var target = Path.Combine(dir, name);
        if (File.Exists(target))
        {
            var stem = Path.GetFileNameWithoutExtension(name);
            var ext = Path.GetExtension(name);
            for (var i = 2; File.Exists(target); i++)
                target = Path.Combine(dir, $"{stem} ({i}){ext}");
        }
        File.Copy(sourcePath, target);
        return Path.GetFileName(target);
    }

    public static string InstanceDirOf(string instanceId)
        => Path.Combine(Boot.Runtime.InstancesDir, instanceId);

    // ------------------------------------------------------------ Saves (stage 4 wrap-up)

    /// <summary>A save entry.</summary>
    /// <param name="Name">World name (directory name under saves)</param>
    /// <param name="LastPlayed">Last write time of the directory</param>
    /// <param name="SizeMb">Size of the world directory</param>
    public sealed record SaveInfo(string Name, DateTimeOffset LastPlayed, long SizeMb);

    public static IReadOnlyList<SaveInfo> ListSaves(string instanceId)
    {
        var dir = Path.Combine(InstanceDirOf(instanceId), "saves");
        var list = new List<SaveInfo>();
        if (!Directory.Exists(dir))
            return list;

        foreach (var world in Directory.EnumerateDirectories(dir))
        {
            var size = 0L;
            try
            {
                foreach (var f in Directory.EnumerateFiles(world, "*", SearchOption.AllDirectories))
                    size += new FileInfo(f).Length;
            }
            catch (IOException) { }

            list.Add(new SaveInfo(Path.GetFileName(world),
                new DateTimeOffset(Directory.GetLastWriteTime(world)), size / (1024 * 1024)));
        }

        return list.OrderByDescending(s => s.LastPlayed).ToList();
    }

    /// <summary>Back up the saves directory as a zip (backups/saves-yyyyMMdd-HHmmss.zip); returns the backup path, or null when there are no saves.</summary>
    public static string? BackupSaves(string instanceId, CancellationToken cancellation = default)
    {
        var savesDir = Path.Combine(InstanceDirOf(instanceId), "saves");
        if (!Directory.Exists(savesDir) || !Directory.EnumerateFileSystemEntries(savesDir).Any())
            return null;

        var backupDir = Path.Combine(InstanceDirOf(instanceId), "backups");
        Directory.CreateDirectory(backupDir);
        var target = Path.Combine(backupDir, $"saves-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(savesDir, target);
        return target;
    }
}
