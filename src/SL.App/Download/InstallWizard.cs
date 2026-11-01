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
using SL.Core.Boot;
using SL.Core.Launch;
using SL.Core.Net;
using SL.Core.Transfers;
using SL.App.Infrastructure;

namespace SL.App.Download;

/// <summary>
/// 安装向导的跨页状态:版本清单页选版本 → 加载器页选加载器 → 安装概览页提交。
/// 与下载引擎的对接全部走 ITransferService,落盘位置遵循实例三层目录约定。
/// </summary>
public static class InstallWizard
{
    public static string VersionId { get; private set; } = "";
    public static string VersionUrl { get; private set; } = "";
    public static string? VersionSha1 { get; private set; }
    public static string VersionType { get; private set; } = "release";
    public static string Loader { get; private set; } = "原版";
    public static MojangMeta.VersionDetail? Detail { get; set; }

    public static bool HasVersion => VersionId.Length > 0;

    public static void ChooseVersion(MojangMeta.VersionEntry entry)
    {
        VersionId = entry.Id;
        VersionUrl = entry.Url;
        VersionSha1 = entry.Sha1;
        VersionType = entry.Type;
        Detail = null;
    }

    public static void ChooseLoader(string loader) => Loader = loader;

    public static void Reset()
    {
        VersionId = "";
        VersionUrl = "";
        VersionSha1 = null;
        VersionType = "release";
        Loader = "原版";
        Detail = null;
    }

    /// <summary>官方域 → BMCLAPI / Modrinth 镜像映射(仅在主源失败或镜像优先时使用)。</summary>
    public static string? MirrorOf(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        var modrinth = Modrinth.MirrorOf(url);
        if (modrinth is not null)
            return modrinth;

        return url switch
        {
            var u when u.Contains("piston-meta.mojang.com", StringComparison.Ordinal)
                => u.Replace("piston-meta.mojang.com", "bmclapi2.bangbang93.com", StringComparison.Ordinal),
            var u when u.Contains("piston-data.mojang.com", StringComparison.Ordinal)
                => u.Replace("piston-data.mojang.com", "bmclapi2.bangbang93.com", StringComparison.Ordinal),
            var u when u.Contains("launchermeta.mojang.com", StringComparison.Ordinal)
                => u.Replace("launchermeta.mojang.com", "bmclapi2.bangbang93.com", StringComparison.Ordinal),
            var u when u.Contains("libraries.minecraft.net", StringComparison.Ordinal)
                => u.Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven", StringComparison.Ordinal),
            var u when u.Contains("maven.minecraftforge.net/", StringComparison.Ordinal)
                => u.Replace("https://maven.minecraftforge.net/", "https://bmclapi2.bangbang93.com/maven/", StringComparison.Ordinal),
            var u when u.Contains("maven.neoforged.net/releases/", StringComparison.Ordinal)
                => u.Replace("https://maven.neoforged.net/releases/", "https://bmclapi2.bangbang93.com/maven/", StringComparison.Ordinal),
            var u when u.Contains("resources.download.minecraft.net", StringComparison.Ordinal)
                => u.Replace("https://resources.download.minecraft.net", "https://bmclapi2.bangbang93.com/assets", StringComparison.Ordinal),
            _ => null,
        };
    }

    private static bool MirrorFirst => AppServices.Settings.Current.DownloadSource == "bmclapi";

    /// <summary>按版本元数据生成安装任务并提交引擎;返回提交的任务数。</summary>
    public static int SubmitInstall(MojangMeta.VersionDetail detail)
    {
        var id = detail.Id;
        var instanceMetaDir = Path.Combine(Runtime.InstancesDir, id, "meta");
        var versionDir = Path.Combine(Runtime.InstancesDir, id, "versions");
        var indexesDir = Path.Combine(Runtime.AssetsDir, "indexes");
        var count = 0;

        if (VersionUrl.Length > 0)
        {
            Submit($"元数据 {id}.json", VersionUrl, VersionSha1, -1,
                Path.Combine(instanceMetaDir, id + ".json"), TransferPriority.CoreGameFile);
            count++;
        }

        if (detail.Client is { } client)
        {
            Submit($"游戏核心 {id}.jar", client.Url, client.Sha1, client.Size,
                Path.Combine(versionDir, id + ".jar"), TransferPriority.CoreGameFile);
            count++;
        }

        foreach (var lib in detail.Libraries)
        {
            Submit("库 " + Path.GetFileName(lib.RelativePath), lib.Url, lib.Sha1, lib.Size,
                Path.Combine(Runtime.LibrariesDir, lib.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                TransferPriority.Library);
            count++;
        }

        if (detail.AssetIndex is { } assetIndex)
        {
            Submit($"资源索引 {detail.AssetId}.json", assetIndex.Url, assetIndex.Sha1, assetIndex.Size,
                Path.Combine(indexesDir, detail.AssetId + ".json"), TransferPriority.AssetIndex);
            count++;
        }

        return count;
    }

    /// <summary>把单个文件(模组/光影/资源包)提交到实例对应目录。</summary>
    public static void SubmitInstanceFile(string instanceId, string subDir, string fileName,
        string url, string? sha1, long size)
    {
        var dest = Path.Combine(Runtime.InstancesDir, instanceId, subDir, fileName);
        Submit(fileName, url, sha1, size, dest, TransferPriority.Normal);
    }

    /// <summary>提交加载器安装所需的库文件(由 LoaderInstall 生成的清单)。</summary>
    public static int SubmitLoaderFiles(IReadOnlyList<LoaderFile> files)
    {
        var count = 0;
        foreach (var file in files)
        {
            Submit(file.DisplayName, file.Url, file.Sha1, file.Size,
                file.DestPath, file.IsLibrary ? TransferPriority.Library : TransferPriority.Normal);
            count++;
        }
        return count;
    }

    private static void Submit(string name, string url, string? sha1, long size, string dest, int priority)
    {
        var sources = new List<TransferSourceSpec>();
        var mirror = MirrorOf(url);
        // Modrinth 走镜像时同样镜像优先
        var mirrorFirst = MirrorFirst
                          || (mirror is not null && Modrinth.MirrorOf(url) is not null && Modrinth.PreferMirror);
        if (mirrorFirst && mirror is not null)
        {
            sources.Add(new(mirror, isMirror: false));
            sources.Add(new(url, isMirror: true));
        }
        else
        {
            sources.Add(new(url, isMirror: false));
            if (mirror is not null)
                sources.Add(new(mirror, isMirror: true));
        }

        AppServices.Transfers.Submit(new TransferSpec
        {
            DisplayName = name,
            Kind = TransferKind.Http,
            Priority = priority,
            BytesTotal = size,
            ExpectedSha1 = string.IsNullOrWhiteSpace(sha1) ? null : sha1,
            DestPath = dest,
            Sources = sources,
        });
    }
}