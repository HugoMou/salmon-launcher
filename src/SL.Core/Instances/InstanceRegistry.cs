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
using System.IO;

namespace SL.Core.Instances;

/// <summary>已安装实例(磁盘事实,非演示)。</summary>
/// <param name="InstanceId">目录名 = 版本 ID</param>
/// <param name="InstalledAt">核心 jar 落盘时间</param>
/// <param name="SizeMb">实例目录近似体积</param>
public sealed record InstanceInfo(string InstanceId, DateTimeOffset InstalledAt, long SizeMb);

/// <summary>实例内的模组(mods 目录下的 .jar / .disabled)。</summary>
/// <param name="FileName">实际文件名(含 .disabled 后缀)</param>
/// <param name="BaseName">去掉 .disabled 的显示名</param>
/// <param name="SizeBytes"></param>
/// <param name="Enabled">.disabled 后缀不存在即为启用</param>
/// <param name="ModifiedAt"></param>
public sealed record ModInfo(string FileName, string BaseName, long SizeBytes, bool Enabled, DateTimeOffset ModifiedAt);

/// <summary>
/// 阶段四:实例注册表。阶段二建立的三层目录事实为准;
/// 模组文件启用/停用(改 .disabled 后缀)/删除/导入都在本层收口,Pres 只调用。
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

    /// <summary>最近安装完成的实例(启动流水线默认目标)。</summary>
    public static InstanceInfo? Latest()
    {
        var all = List();
        return all.Count > 0 ? all[0] : null;
    }

    // ------------------------------------------------------------ 模组

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

    /// <summary>切启用/停用:追加或去掉 .disabled 后缀(文件系统即状态)。返回切换后的启用态。</summary>
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
            return true; // 重命名后即启用
        }

        var disabled = path + ".disabled";
        if (File.Exists(disabled))
            throw new IOException($"目标已存在:{Path.GetFileName(disabled)}");
        Directory.Move(path, disabled);
        return false; // 停用成功
    }

    public static void DeleteMod(string instanceId, string fileName)
    {
        var path = Path.Combine(ModsDirOf(instanceId), fileName);
        if (File.Exists(path))
            File.Delete(path);
    }

    /// <summary>从本地导入(复制,不移动;同名 → 覆盖前重命名 带序号)。</summary>
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

    // ------------------------------------------------------------ 存档(阶段四收尾)

    /// <summary>存档条目。</summary>
    /// <param name="Name">世界名(saves 下的目录名)</param>
    /// <param name="LastPlayed">目录最近写入时间</param>
    /// <param name="SizeMb">世界目录体积</param>
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

    /// <summary>备份 saves 目录为 zip(backups/saves-yyyyMMdd-HHmmss.zip);返回备份路径;无存档返回 null。</summary>
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
