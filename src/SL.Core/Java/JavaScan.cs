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

namespace SL.Core.Java;

/// <summary>已安装的 Java 运行时。</summary>
/// <param name="HomePath">JAVA_HOME 目录(包含 bin/java.exe)</param>
/// <param name="VersionText">如 "21.0.4"、"17.0.2"</param>
/// <param name="Vendor">如 "Eclipse Adoptium"</param>
/// <param name="Architecture">"x64" / "aarch64"</param>
public sealed record JavaInstall(string HomePath, string VersionText, string Vendor, string Architecture)
{
    /// <summary>主版本号(解析失败为 0;Minecraft 1.20+ 需要 17+,1.20.5+ 需要 21+)。</summary>
    public int MajorVersion
    {
        get
        {
            var head = VersionText.Split('.', '+')[0];
            return int.TryParse(head, out var v) ? v : 0;
        }
    }
}

/// <summary>
/// 本机 Java 自动扫描(阶段二):遍历常见安装位置,读 release 文件判定版本,
/// 全程零进程启动(毫秒级完成,不触发 java.exe)。
/// </summary>
public static class JavaScan
{
    public static async Task<IReadOnlyList<JavaInstall>> ScanAsync(CancellationToken cancellation)
    {
        return await Task.Run(() => ScanCore(cancellation), cancellation).ConfigureAwait(false);
    }

    private static List<JavaInstall> ScanCore(CancellationToken cancellation)
    {
        var candidates = CollectCandidateHomes();
        var found = new Dictionary<string, JavaInstall>(StringComparer.OrdinalIgnoreCase);

        foreach (var home in candidates)
        {
            cancellation.ThrowIfCancellationRequested();

            var exe = Path.Combine(home, "bin", "java.exe");
            var releaseFile = Path.Combine(home, "release");
            if (!File.Exists(exe) || !File.Exists(releaseFile))
                continue;

            var info = ParseReleaseFile(releaseFile);
            if (info is null)
                continue;

            found[home] = info with { HomePath = home };
        }

        return found.Values
            .OrderByDescending(j => j.MajorVersion)
            .ThenBy(j => j.HomePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> CollectCandidateHomes()
    {
        var results = new List<string>();

        void AddRootsFrom(string? root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return;
            try
            {
                results.AddRange(Directory.EnumerateDirectories(root));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // JAVA_HOME 本身(直接是 home 而非父目录)
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome) && File.Exists(Path.Combine(javaHome, "bin", "java.exe")))
            results.Add(javaHome);

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        AddRootsFrom(Path.Combine(programFiles, "Java"));
        AddRootsFrom(Path.Combine(programFiles, "Eclipse Adoptium"));
        AddRootsFrom(Path.Combine(programFiles, "Microsoft"));
        AddRootsFrom(Path.Combine(programFiles, "Zulu"));
        AddRootsFrom(Path.Combine(programFilesX86, "Java"));
        AddRootsFrom(Path.Combine(localAppData, "Programs", "Eclipse Adoptium"));
        AddRootsFrom(Path.Combine(userProfile, ".jdks"));
        AddRootsFrom(Path.Combine(userProfile, "scoop", "apps", "java", "current", ".."));

        // SL 自家的 java 目录(阶段三自动下载的 temurin-<major> 就装在这里)
        AddRootsFrom(Path.Combine(Boot.Runtime.Root, "java"));

        return results.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>解析 JDK 自带的 release 文本:JAVA_VERSION="21.0.4" / IMPLEMENTOR="..." / OS_ARCH="x64"。</summary>
    private static JavaInstall? ParseReleaseFile(string releaseFile)
    {
        try
        {
            string? version = null, vendor = null, arch = null;
            foreach (var raw in File.ReadLines(releaseFile))
            {
                var eq = raw.IndexOf('=');
                if (eq <= 0)
                    continue;

                var key = raw[..eq];
                var value = raw[(eq + 1)..].Trim().Trim('"');

                if (key == "JAVA_VERSION")
                    version = value;
                else if (key == "IMPLEMENTOR")
                    vendor = value;
                else if (key == "OS_ARCH")
                    arch = value is "amd64" ? "x64" : value;
            }

            if (version is null)
                return null;
            return new JavaInstall("", version, vendor ?? "未知发行版", arch ?? "x64");
        }
        catch (IOException)
        {
            return null;
        }
    }
}
