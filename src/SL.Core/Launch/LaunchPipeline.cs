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
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SL.Core.Launch;

/// <summary>Launch result.</summary>
/// <param name="Success">Whether the process was started successfully</param>
/// <param name="ProcessId">Game process PID (0 on failure)</param>
/// <param name="Error">Failure reason</param>
public sealed record LaunchResult(bool Success, int ProcessId, string? Error);

/// <summary>
/// Stage 3: the real Minecraft launch pipeline.
/// Version resolution → library/dependency check (missing ones filled from the primary source + mirror) → natives extraction → differential asset fill
/// → Java lookup (auto-download Temurin when missing) → argument assembly (legacy/current argument formats) → process start (output redirected to the instance log).
/// </summary>
public static class LaunchPipeline
{
    /// <summary>Step log (the strings can be shown to the user as-is).</summary>
    public static event Action<string>? Step;

    /// <summary>Game process exited (for diagnostics).</summary>
    public static event Action<int>? GameExited;

    /// <summary>Instance discovery (the minimal stage-3 capability): ID of the most recently installed instance on disk (one that has a core jar).</summary>
    public static string? LatestInstanceId()
    {
        var root = Boot.Runtime.InstancesDir;
        if (!Directory.Exists(root))
            return null;

        string? best = null;
        var bestTime = DateTime.MinValue;
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var id = Path.GetFileName(dir);
            var jar = Path.Combine(dir, "versions", id + ".jar");
            if (!File.Exists(jar))
                continue;
            var t = File.GetLastWriteTimeUtc(jar);
            if (t > bestTime)
            {
                bestTime = t;
                best = id;
            }
        }
        return best;
    }

    private static void Raise(string message)
    {
        Core.Logging.AppLog.Info("Launch", message);
        try { Step?.Invoke(message); }
        catch { /* a subscriber exception must not abort the pipeline */ }
    }

    /// <summary>Launch identity: premium (msa) passes a real uuid/token; offline passes null (falls back to OfflineUuid).</summary>
    public sealed record LaunchIdentity(string Name, string Uuid, string AccessToken, string UserType);

    public static async Task<LaunchResult> StartAsync(
        string instanceId,
        string accountName,
        int memoryMb,
        CancellationToken cancellation,
        LaunchIdentity? identity = null,
        string jvmExtraArgs = "",
        int gameWidth = 0,
        int gameHeight = 0)
    {
        var instanceDir = Path.Combine(Boot.Runtime.InstancesDir, instanceId);
        var metaFile = Path.Combine(instanceDir, "meta", instanceId + ".json");
        var clientJar = Path.Combine(instanceDir, "versions", instanceId + ".jar");

        // ============================== Version resolution ==============================
        Raise("解析版本元数据…");
        if (!File.Exists(metaFile))
            return new LaunchResult(false, 0, $"实例 {instanceId} 缺少版本元数据({metaFile})");
        if (!File.Exists(clientJar))
            return new LaunchResult(false, 0, $"实例 {instanceId} 缺少游戏核心 jar");

        Core.Net.MojangMeta.VersionDetail detail;
        JsonElement root;
        using (var doc = JsonDocument.Parse(await File.ReadAllTextAsync(metaFile, cancellation).ConfigureAwait(false)))
        {
            root = doc.RootElement.Clone();
        }
        detail = Core.Net.MojangMeta.ParseVersionJson(
            await File.ReadAllTextAsync(metaFile, cancellation).ConfigureAwait(false));
        if (detail.Client is null || detail.MainClass.Length == 0)
            return new LaunchResult(false, 0, "版本 JSON 不完整(缺少 client 或 mainClass)");

        // ============================== Client core jar integrity ==============================
        // An interrupted download or a killed process leaves a file that "exists but is not a valid jar"; launching it only yields
        // ClassNotFoundException; so validate size/SHA-1/zip structure first and re-download automatically when it fails.
        if (!IsValidClientJar(clientJar, detail.Client.Sha1, detail.Client.Size))
        {
            Raise("游戏核心 jar 不完整,正在重新下载…");
            var temp = clientJar + ".download";
            var ok = await FetchToFileAsync(detail.Client.Url, temp, cancellation).ConfigureAwait(false)
                     || await FetchToFileAsync(MirrorOf(detail.Client.Url), temp, cancellation).ConfigureAwait(false);
            if (!ok || !IsValidClientJar(temp, detail.Client.Sha1, detail.Client.Size))
            {
                if (File.Exists(temp))
                    File.Delete(temp);
                return new LaunchResult(false, 0, "游戏核心 jar 校验失败(下载不完整,请检查网络后重试)");
            }
            File.Move(temp, clientJar, overwrite: true);
            Raise("游戏核心 jar 已修复");
        }

        // ============================== Library/dependency check ==============================
        Raise($"校验 {detail.Libraries.Count} 个库文件…");
        var missingLibs = detail.Libraries
            .Where(l => !string.IsNullOrEmpty(l.RelativePath))
            .Where(l =>
            {
                var p = Path.Combine(Boot.Runtime.LibrariesDir, l.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(p))
                    return true;
                var actual = new FileInfo(p).Length;
                return actual == 0 || (l.Size > 0 && actual != l.Size);
            })
            .ToList();

        if (missingLibs.Count > 0)
        {
            Raise($"补齐缺失库 {missingLibs.Count} 个…");
            foreach (var lib in missingLibs)
            {
                cancellation.ThrowIfCancellationRequested();
                var target = Path.Combine(Boot.Runtime.LibrariesDir, lib.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var fetched = await FetchToFileAsync(lib.Url, target, cancellation).ConfigureAwait(false)
                              || await FetchToFileAsync(MirrorOf(lib.Url), target, cancellation).ConfigureAwait(false);
                if (!fetched)
                    return new LaunchResult(false, 0, $"库缺失且补齐失败:{lib.RelativePath}");
            }
        }

        // ============================== natives extraction ==============================
        var nativesDir = Path.Combine(instanceDir, "natives");
        if (Directory.Exists(nativesDir))
        {
            // Rebuild everything on each launch so stale natives (e.g. an ARM64 build installed by mistake in the past) never linger
            Directory.Delete(nativesDir, recursive: true);
        }
        Directory.CreateDirectory(nativesDir);
        // 2026 argument layout: -Djava.library.path=<natives>/java; jna/lwjgl/netty each have their own subdirectory
        var nativesJavaDir = Path.Combine(nativesDir, "java");
        Directory.CreateDirectory(nativesJavaDir);
        foreach (var sub in new[] { "jna", "lwjgl", "netty" })
            Directory.CreateDirectory(Path.Combine(nativesDir, sub));
        var nativeLibs = detail.Libraries.Where(l => l.IsNative).ToList();
        Raise($"解压 {nativeLibs.Count} 个 Windows natives…");
        foreach (var native in nativeLibs)
        {
            cancellation.ThrowIfCancellationRequested();
            var jar = Path.Combine(Boot.Runtime.LibrariesDir, native.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(jar))
                return new LaunchResult(false, 0, $"natives 包缺失:{native.RelativePath}");
            ExtractNatives(jar, nativesDir);
        }
        // Collect every dll into <natives>/java (where java.library.path actually points)
        foreach (var dll in Directory.EnumerateFiles(nativesDir, "*.dll", SearchOption.AllDirectories))
        {
            var target = Path.Combine(nativesJavaDir, Path.GetFileName(dll));
            if (!File.Exists(target))
                File.Copy(dll, target);
        }

        // ============================== Differential asset fill ==============================
        var assetIndexFile = Path.Combine(Boot.Runtime.AssetsDir, "indexes", detail.AssetId + ".json");
        if (detail.AssetIndex is not null && File.Exists(assetIndexFile))
        {
            Raise("检查资源对象差异…");
            int missingAssets;
            void OnAssetProgress(Launch.AssetEnsure.AssetProgress p)
            {
                if (p.Done % 64 == 0 || p.Done == p.Total)
                    Raise($"补齐资源对象 {p.Done}/{p.Total}…");
            }
            Launch.AssetEnsure.Progress += OnAssetProgress;
            try
            {
                missingAssets = await Launch.AssetEnsure.EnsureAsync(assetIndexFile, Boot.Runtime.AssetsDir, cancellation)
                    .ConfigureAwait(false);
            }
            finally
            {
                Launch.AssetEnsure.Progress -= OnAssetProgress;
            }
            if (missingAssets > 0)
                Raise($"资源对象补齐完成({missingAssets} 个文件)");
        }

        // ============================== Java lookup ==============================
        Raise($"定位 Java {detail.JavaMajor}+ …");
        var javaHome = await Launch.JavaFetch.EnsureJavaAsync(detail.JavaMajor, cancellation).ConfigureAwait(false);
        if (string.IsNullOrEmpty(javaHome))
            return new LaunchResult(false, 0, $"未找到 Java {detail.JavaMajor}+,自动获取失败(检查网络)");
        var javaExe = Path.Combine(javaHome, "bin", "javaw.exe");
        if (!File.Exists(javaExe))
            javaExe = Path.Combine(javaHome, "bin", "java.exe");

        // ============================== Argument assembly ==============================
        Raise("组装启动参数…");
        var classpath = BuildClasspath(detail, clientJar);
        Logging.AppLog.Info("Launch",
            $"mainClass={detail.MainClass} · 库 {detail.Libraries.Count} 项 · classpath {classpath.Split(';').Length} 段");
        var args = BuildArgs(root, detail, classpath, instanceId, instanceDir, nativesDir, accountName, memoryMb, identity);
        args.Insert(0, $"-Xmx{memoryMb}M");
        foreach (var extra in jvmExtraArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            args.Insert(1, extra);
        if (gameWidth > 0 && gameHeight > 0)
        {
            args.Add("--width");
            args.Add(gameWidth.ToString());
            args.Add("--height");
            args.Add(gameHeight.ToString());
        }
        var gameDir = instanceDir;

        // ============================== Process start ==============================
        Raise("启动游戏进程…");
        var psi = new ProcessStartInfo
        {
            FileName = javaExe,
            WorkingDirectory = gameDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        Directory.CreateDirectory(Path.Combine(gameDir, "logs"));
        // Do not take over the game own latest.log: console output goes to a separate file,
        // and shared read/write is allowed so a previous process that has not exited yet does not block launching.
        var consoleLogPath = Path.Combine(gameDir, "logs", "launcher-console.log");
        var logStream = new StreamWriter(
            new FileStream(consoleLogPath,
                FileMode.Create, FileAccess.Write, FileShare.ReadWrite, 1 << 14, FileOptions.Asynchronous))
        { AutoFlush = true };

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            logStream.Dispose();
            return new LaunchResult(false, 0, $"进程启动失败:{ex.Message}");
        }
        if (process is null)
        {
            logStream.Dispose();
            return new LaunchResult(false, 0, "进程启动失败(返回空进程)");
        }

        _ = PumpOutputAsync(process.StandardOutput, logStream, cancellation);
        _ = PumpOutputAsync(process.StandardError, logStream, cancellation);
        process.Exited += (_, _) =>
        {
            try { logStream.Flush(); } catch { }
            Raise($"游戏进程退出(ExitCode {process.ExitCode})");
            GameExited?.Invoke(process.ExitCode);
        };
        process.EnableRaisingEvents = true;

        Raise($"Minecraft {instanceId} 已启动(PID {process.Id})");
        return new LaunchResult(true, process.Id, null);
    }

    // ------------------------------------------------------------ Internals

    private static async Task PumpOutputAsync(StreamReader reader, StreamWriter sink, CancellationToken ct)
    {
        try
        {
            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                await sink.WriteLineAsync(line).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { }
    }

    private static string? MirrorOf(string? url)
    {
        if (string.IsNullOrEmpty(url))
            return null;

        // BMCLAPI official mapping: replace the host, keep the path; asset objects use a special segment
        return url switch
        {
            var u when u.Contains("piston-meta.mojang.com", StringComparison.Ordinal)
                => u.Replace("piston-meta.mojang.com", "bmclapi2.bangbang93.com", StringComparison.Ordinal),
            var u when u.Contains("piston-data.mojang.com", StringComparison.Ordinal)
                => u.Replace("piston-data.mojang.com", "bmclapi2.bangbang93.com", StringComparison.Ordinal),
            var u when u.Contains("launchermeta.mojang.com", StringComparison.Ordinal)
                => u.Replace("launchermeta.mojang.com", "bmclapi2.bangbang93.com", StringComparison.Ordinal),
            var u when u.Contains("launcher.mojang.com", StringComparison.Ordinal)
                => u.Replace("launcher.mojang.com", "bmclapi2.bangbang93.com", StringComparison.Ordinal),
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

    /// <summary>Only official domains and the BMCLAPI mirror may be downloaded to disk (safety guard).</summary>
    private static bool IsAllowedSource(string? url) => !string.IsNullOrEmpty(url) && (url
            .Contains(".mojang.com", StringComparison.Ordinal)
            || url.Contains("minecraft.net", StringComparison.Ordinal)
            || url.Contains("bmclapi2.bangbang93.com", StringComparison.Ordinal)
            || url.Contains("api.adoptium.net", StringComparison.Ordinal)
            || url.Contains("github.com", StringComparison.Ordinal)
            || url.Contains("piston-meta", StringComparison.Ordinal)
            // Loader-related repositories (Fabric / Quilt / Forge / NeoForge / Maven Central)
            || url.Contains("meta.fabricmc.net", StringComparison.Ordinal)
            || url.Contains("maven.fabricmc.net", StringComparison.Ordinal)
            || url.Contains("meta.quiltmc.org", StringComparison.Ordinal)
            || url.Contains("maven.quiltmc.org", StringComparison.Ordinal)
            || url.Contains("maven.minecraftforge.net", StringComparison.Ordinal)
            || url.Contains("maven.neoforged.net", StringComparison.Ordinal)
            || url.Contains("repo1.maven.org", StringComparison.Ordinal)
            || url.Contains("libraries.minecraft.net", StringComparison.Ordinal));

    /// <summary>Whether the client jar is complete and usable: size, SHA-1 and zip structure (must contain the main class).</summary>
    private static bool IsValidClientJar(string path, string? sha1, long size)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < 64 * 1024)
                return false;
            if (size > 0 && info.Length != size)
                return false;
            if (!string.IsNullOrWhiteSpace(sha1) && !string.Equals(Sha1Of(path), sha1, StringComparison.OrdinalIgnoreCase))
                return false;

            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            return zip.Entries.Any(e => e.FullName.EndsWith("client/main/Main.class", StringComparison.Ordinal));
        }
        catch
        {
            return false;
        }
    }

    private static string Sha1Of(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA1.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static async Task<bool> FetchToFileAsync(string? url, string target, CancellationToken ct)
    {
        if (!IsAllowedSource(url))
            return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            var bytes = await client.GetByteArrayAsync(url!, ct).ConfigureAwait(false);
            await File.WriteAllBytesAsync(target, bytes, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return false; }
    }

    private static void ExtractNatives(string jarPath, string nativesDir)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(jarPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith("/"))
                continue;
            // Keep the sub-paths inside the jar (jna / lwjgl / deep structures); root-level dlls are copied flat into nativesDir
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (relative.StartsWith('\\'))
                relative = relative.TrimStart('\\');
            var target = Path.Combine(nativesDir, relative);
            var parent = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(parent))
                Directory.CreateDirectory(parent);
            using var src = entry.Open();
            using var dst = File.Create(target);
            src.CopyTo(dst);
        }
    }

    private static string BuildClasspath(Core.Net.MojangMeta.VersionDetail detail, string clientJar)
    {
        var entries = new List<string> { clientJar };
        entries.AddRange(detail.Libraries
            .Where(l => !l.IsNative && l.RelativePath.Length > 0)
            .Select(l => Path.Combine(Boot.Runtime.LibrariesDir, l.RelativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Distinct(StringComparer.OrdinalIgnoreCase));
        return string.Join(";", entries);
    }

    private static List<string> BuildArgs(
        JsonElement root,
        Core.Net.MojangMeta.VersionDetail detail,
        string classpath,
        string instanceId,
        string instanceDir,
        string nativesDir,
        string account,
        int memoryMb,
        LaunchIdentity? identity)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["${auth_player_name}"] = account,
            ["${auth_uuid}"] = Launch.JavaFetch.OfflineUuid(account),
            ["${auth_access_token}"] = "0",
            ["${auth_xuid}"] = "",
            ["${clientid}"] = "",
            ["${user_type}"] = "legacy",
            ["${version_name}"] = detail.Id,
            ["${version_type}"] = detail.Type,
            ["${assets_root}"] = Boot.Runtime.AssetsDir,
            ["${game_assets}"] = Path.Combine(Boot.Runtime.AssetsDir, "virtual", detail.AssetId),
            ["${assets_index_name}"] = detail.AssetId,
            ["${game_directory}"] = instanceDir,
            ["${natives_directory}"] = nativesDir,
            ["${launcher_name}"] = "SalmonLauncher",
            ["${launcher_version}"] = Boot.Runtime.AppVersion.Split(' ', 2)[0],
            ["${classpath}"] = classpath,
            ["${classpath_separator}"] = ";",
            ["${library_directory}"] = Boot.Runtime.LibrariesDir,
            ["${quick_play_path}"] = "",
            ["${user_properties}"] = "{}",
        };

        // Premium identity overrides the offline default (name/uuid/real token/msa type)
        if (identity is not null)
        {
            map["${auth_player_name}"] = identity.Name;
            map["${auth_uuid}"] = identity.Uuid;
            map["${auth_access_token}"] = identity.AccessToken;
            map["${user_type}"] = identity.UserType;
        }

        var args = new List<string>();

        var natives = nativesDir.Replace(Path.DirectorySeparatorChar, '/'); // Windows accepts this as well
        args.Add($"-Djava.library.path={natives}");

        if (root.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.Object)
        {
            // —— JVM arguments (new format) ——
            if (arguments.TryGetProperty("jvm", out var jvm) && jvm.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in jvm.EnumerateArray())
                    AppendArgument(args, item, map);
            }
            else
            {
                // legacy format fallback
                args.Add("-cp");
                args.Add(classpath);
            }

            args.Add(detail.MainClass);

            // —— Game arguments ——
            if (arguments.TryGetProperty("game", out var game) && game.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in game.EnumerateArray())
                    AppendArgument(args, item, map);
            }
        }
        else if (root.TryGetProperty("minecraftArguments", out var oldArgs) && oldArgs.GetString() is { } legacy)
        {
            args.Add("-cp");
            args.Add(classpath);
            args.Add(detail.MainClass);
            foreach (var part in legacy.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                args.Add(Substitute(part, map));
        }
        else
        {
            args.Add("-cp");
            args.Add(classpath);
            args.Add(detail.MainClass);
        }

        return args;
    }

    private static void AppendArgument(List<string> args, JsonElement item, Dictionary<string, string> map)
    {
        switch (item.ValueKind)
        {
            case JsonValueKind.String:
                args.Add(Substitute(item.GetString() ?? "", map));
                break;

            case JsonValueKind.Object:
            {
                // {rules, value / values}
                if (item.TryGetProperty("rules", out var rules) && !EvaluateArgsRules(rules))
                    return;

                if (item.TryGetProperty("value", out var value))
                {
                    if (value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var v in value.EnumerateArray())
                            if (v.ValueKind == JsonValueKind.String)
                                args.Add(Substitute(v.GetString() ?? "", map));
                    }
                    else if (value.ValueKind == JsonValueKind.String)
                    {
                        args.Add(Substitute(value.GetString() ?? "", map));
                    }
                }
                else if (item.TryGetProperty("values", out var values))
                {
                    if (values.ValueKind == JsonValueKind.Array)
                        foreach (var v in values.EnumerateArray())
                            if (v.ValueKind == JsonValueKind.String)
                                args.Add(Substitute(v.GetString() ?? "", map));
                }
                break;
            }
        }
    }

    /// <summary>windows / no-feature rule evaluation for arguments.* (same strategy as library rules, stage 3).
    /// Semantics match the official launcher: enabled only when at least one allow rule matches and no deny rule matches; no match → skipped.</summary>
    private static bool EvaluateArgsRules(JsonElement rules)
    {
        bool? allowed = null;
        foreach (var rule in rules.EnumerateArray())
        {
            var action = rule.TryGetProperty("action", out var a) && a.GetString() == "deny" ? "deny" : "allow";

            bool applies = true;
            if (rule.TryGetProperty("os", out var os) && os.ValueKind == JsonValueKind.Object)
            {
                if (os.TryGetProperty("name", out var n))
                    applies &= n.GetString() is "windows" or null or "";
                if (os.TryGetProperty("arch", out var arch))
                    applies &= arch.GetString() is null or "" or "x64";
                if (os.TryGetProperty("version", out var ver))
                    applies &= ver.GetString() is null or "";
            }
            if (rule.TryGetProperty("features", out _))
                applies = false;

            if (!applies)
                continue;

            if (action == "allow")
                allowed = true;
            else
                allowed = false;
        }

        return allowed == true;
    }

    private static string Substitute(string input, Dictionary<string, string> map)
        => map.TryGetValue(input, out var mapped) ? mapped : input
            .Replace("${auth_player_name}", map["${auth_player_name}"], StringComparison.Ordinal)
            .Replace("${auth_uuid}", map["${auth_uuid}"], StringComparison.Ordinal)
            .Replace("${version_name}", map["${version_name}"], StringComparison.Ordinal)
            .Replace("${version_type}", map["${version_type}"], StringComparison.Ordinal)
            .Replace("${assets_root}", map["${assets_root}"], StringComparison.Ordinal)
            .Replace("${game_assets}", map["${game_assets}"], StringComparison.Ordinal)
            .Replace("${assets_index_name}", map["${assets_index_name}"], StringComparison.Ordinal)
            .Replace("${game_directory}", map["${game_directory}"], StringComparison.Ordinal)
            .Replace("${classpath}", map["${classpath}"], StringComparison.Ordinal)
            .Replace("${classpath_separator}", ";", StringComparison.Ordinal)
            .Replace("${library_directory}", map["${library_directory}"], StringComparison.Ordinal)
            .Replace("${natives_directory}", map["${natives_directory}"], StringComparison.Ordinal)
            .Replace("${launcher_name}", "SalmonLauncher", StringComparison.Ordinal)
            .Replace("${launcher_version}", map["${launcher_version}"], StringComparison.Ordinal);
}
