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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SL.Core.Launch;

/// <summary>Loader install result: instance id + list of files still to download.</summary>
public sealed record LoaderInstallPlan(
    string InstanceId,
    string MetaPath,
    string JarPath,
    IReadOnlyList<LoaderFile> Files);

/// <summary>One file that the loader installation needs to fill in.</summary>
public sealed record LoaderFile(string DisplayName, string Url, string DestPath, string? Sha1, long Size, bool IsLibrary);

/// <summary>
/// One-click loader installation:
/// - Fabric / Quilt: fetch the official profile JSON and merge it with the vanilla version JSON into launch-ready instance metadata;
/// - Forge / NeoForge: download the official installer, run it in place with --installClient, then merge the version JSON it produces.
/// Both paths finally output "instance metadata + core jar + list of libraries to fill in".
/// </summary>
public static class LoaderInstall
{
    private static readonly HttpClient Http = CreateHttp(TimeSpan.FromSeconds(30));
    private static readonly HttpClient Downloader = CreateHttp(TimeSpan.FromMinutes(10));

    /// <summary>Proxies such as BMCLAPI answer 403 outright to requests without a User-Agent, so it must be sent.</summary>
    private static HttpClient CreateHttp(TimeSpan timeout)
    {
        var client = new HttpClient { Timeout = timeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SalmonLauncher/" + Boot.Runtime.AppVersion);
        return client;
    }

    private const string FabricLoaderList = "https://meta.fabricmc.net/v2/versions/loader";
    private const string FabricProfile = "https://meta.fabricmc.net/v2/versions/loader/{0}/{1}/profile/json";
    private const string QuiltLoaderList = "https://meta.quiltmc.org/v3/versions/loader";
    private const string QuiltProfile = "https://meta.quiltmc.org/v3/versions/loader/{0}/{1}/profile/json";
    private const string ForgeMetadata = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    private const string ForgeInstaller = "https://maven.minecraftforge.net/net/minecraftforge/forge/{0}/forge-{0}-installer.jar";
    private const string NeoForgeMetadata = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
    private const string NeoForgeInstaller = "https://maven.neoforged.net/releases/net/neoforged/neoforge/{0}/neoforge-{0}-installer.jar";

    /// <summary>BMCLAPI maven proxy (also covers Forge / NeoForge / Mojang libraries).</summary>
    private const string BmclMaven = "https://bmclapi2.bangbang93.com/maven";

    /// <summary>Forge / NeoForge maven URL → BMCLAPI proxy; other URLs return null.</summary>
    private static string? MirrorUrl(string url)
    {
        if (url.Contains("maven.minecraftforge.net/", StringComparison.Ordinal))
            return url.Replace("https://maven.minecraftforge.net/", BmclMaven + "/", StringComparison.Ordinal);
        if (url.Contains("maven.neoforged.net/releases/", StringComparison.Ordinal))
            return url.Replace("https://maven.neoforged.net/releases/", BmclMaven + "/", StringComparison.Ordinal);
        if (url.Contains("libraries.minecraft.net/", StringComparison.Ordinal))
            return url.Replace("https://libraries.minecraft.net/", BmclMaven + "/", StringComparison.Ordinal);
        return null;
    }

    /// <summary>Get the latest loader version string (null when unavailable).</summary>
    public static async Task<string?> LatestLoaderAsync(string loader, CancellationToken ct)
    {
        var url = loader.Equals("Quilt", StringComparison.OrdinalIgnoreCase) ? QuiltLoaderList : FabricLoaderList;
        try
        {
            var json = await Http.GetStringAsync(url, ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var stable = item.TryGetProperty("stable", out var s) && s.ValueKind == JsonValueKind.True;
                var version = item.TryGetProperty("version", out var v) ? v.GetString() : null;
                if (stable && !string.IsNullOrWhiteSpace(version))
                    return version;
            }
            // entries not marked stable: take the first one
            if (doc.RootElement.GetArrayLength() > 0
                && doc.RootElement[0].TryGetProperty("version", out var first))
                return first.GetString();
        }
        catch
        {
            // leave the hint to the caller
        }
        return null;
    }

    /// <summary>
    /// Build the merged instance (metadata + a copy of the core jar) and return the list of files to download.
    /// vanillaJson / vanillaJar point at the already installed vanilla instance.
    /// </summary>
    public static async Task<LoaderInstallPlan> BuildPlanAsync(
        string loader, string game, string? loaderVersion,
        string vanillaJsonPath, string vanillaJarPath,
        CancellationToken ct, Action<string>? step = null)
    {
        if (loader.Equals("Forge", StringComparison.OrdinalIgnoreCase)
            || loader.Equals("NeoForge", StringComparison.OrdinalIgnoreCase))
            return await InstallWithOfficialInstallerAsync(loader, game, loaderVersion,
                vanillaJsonPath, vanillaJarPath, step, ct).ConfigureAwait(false);

        step?.Invoke($"正在获取 {loader} 官方 profile…");
        var version = loaderVersion;
        if (string.IsNullOrWhiteSpace(version))
            version = await LatestLoaderAsync(loader, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException($"无法获取 {loader} 版本列表(检查网络)");

        var isQuilt = loader.Equals("Quilt", StringComparison.OrdinalIgnoreCase);
        var profileUrl = string.Format(isQuilt ? QuiltProfile : FabricProfile, game, version);
        var profileJson = await Http.GetStringAsync(profileUrl, ct).ConfigureAwait(false);

        var instanceId = $"{game}-{loader.ToLowerInvariant()}";
        var instanceDir = Path.Combine(Boot.Runtime.InstancesDir, instanceId);
        var metaDir = Path.Combine(instanceDir, "meta");
        var versionDir = Path.Combine(instanceDir, "versions");
        Directory.CreateDirectory(metaDir);
        Directory.CreateDirectory(versionDir);

        var vanillaText = await File.ReadAllTextAsync(vanillaJsonPath, ct).ConfigureAwait(false);
        var merged = Merge(vanillaText, profileJson, instanceId);
        var metaPath = Path.Combine(metaDir, instanceId + ".json");
        await File.WriteAllTextAsync(metaPath, merged.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct)
            .ConfigureAwait(false);

        // the loader has no core jar of its own, so copy the vanilla jar as the instance core
        var jarPath = Path.Combine(versionDir, instanceId + ".jar");
        if (!File.Exists(jarPath))
        {
            File.Copy(vanillaJarPath, jarPath, overwrite: true);
            // the copy would inherit the vanilla jar timestamp; set it to now, otherwise the new instance will not sort first in the instance list
            File.SetLastWriteTimeUtc(jarPath, DateTime.UtcNow);
        }

        var files = CollectFiles(merged);
        return new LoaderInstallPlan(instanceId, metaPath, jarPath, files);
    }

    private static JsonObject Merge(string vanillaJson, string profileJson, string instanceId)
    {
        var vanilla = JsonNode.Parse(vanillaJson)!.AsObject();
        var profile = JsonNode.Parse(profileJson)!.AsObject();

        var merged = new JsonObject
        {
            ["id"] = instanceId,
            ["type"] = "release",
            ["mainClass"] = profile["mainClass"]?.DeepClone() ?? vanilla["mainClass"]?.DeepClone() ?? "",
            ["assets"] = vanilla["assets"]?.DeepClone(),
            ["assetIndex"] = vanilla["assetIndex"]?.DeepClone(),
            ["downloads"] = vanilla["downloads"]?.DeepClone(),
            ["javaVersion"] = vanilla["javaVersion"]?.DeepClone(),
            ["libraries"] = MergeLibraries(vanilla["libraries"], profile["libraries"]),
        };

        // merge the new format (arguments.jvm/game) with vanilla; old Forge only has minecraftArguments, so carry it over unchanged
        var mergedArgs = MergeArguments(vanilla["arguments"], profile["arguments"]);
        if (mergedArgs["jvm"]!.AsArray().Count > 0 || mergedArgs["game"]!.AsArray().Count > 0)
            merged["arguments"] = mergedArgs;
        else if (profile["minecraftArguments"] is { } legacyArgs)
            merged["minecraftArguments"] = legacyArgs.DeepClone();

        return merged;
    }

    /// <summary>Loader arguments first, vanilla arguments after; both sides are in object form { jvm:[], game:[] }.</summary>
    private static JsonObject MergeArguments(JsonNode? vanilla, JsonNode? profile)
    {
        var result = new JsonObject { ["jvm"] = new JsonArray(), ["game"] = new JsonArray() };
        foreach (var (source, first) in new[] { (profile, true), (vanilla, false) })
        {
            if (source is not JsonObject obj)
                continue;
            foreach (var key in new[] { "jvm", "game" })
            {
                if (obj[key] is not JsonArray array)
                    continue;
                foreach (var item in array)
                {
                    if (item is null)
                        continue;
                    ((JsonArray)result[key]!).Add(item.DeepClone());
                }
            }
        }
        return result;
    }

    /// <summary>Loader libraries first (same name overrides vanilla), vanilla libraries after; deduplicate by name.</summary>
    private static JsonArray MergeLibraries(JsonNode? vanilla, JsonNode? profile)
    {
        var result = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(JsonNode? node)
        {
            if (node is not JsonObject lib)
                return;
            var name = lib["name"]?.GetValue<string>() ?? "";
            if (name.Length == 0)
                return;
            // deduplicate by group:artifact[:classifier]: keep only the version the loader specifies for a dependency,
            // otherwise vanilla own asm 9.6 and the loader 9.10.1 would both sit on the classpath and Fabric would refuse to start.
            if (!seen.Add(DependencyKey(name)))
                return;
            result.Add(lib.DeepClone());
        }

        if (profile is JsonArray profileLibs)
            foreach (var lib in profileLibs)
                Add(lib);
        if (vanilla is JsonArray vanillaLibs)
            foreach (var lib in vanillaLibs)
                Add(lib);

        NormalizeArtifacts(result);
        return result;
    }

    /// <summary>Dependency key of a maven coordinate (version ignored, classifier kept).</summary>
    private static string DependencyKey(string name)
    {
        var main = name.Split('@')[0];
        var parts = main.Split(':');
        if (parts.Length < 2)
            return main;
        var classifier = parts.Length > 3 ? parts[3] : "";
        return parts[0] + ":" + parts[1] + ":" + classifier;
    }

    /// <summary>
    /// Fabric / Quilt profiles only give maven coordinates (name + repository root url), without downloads.artifact;
    /// Forge metadata can also contain entries whose artifact lacks a url.
    /// Here the artifact (path/url) is synthesized from the coordinate, otherwise the launcher neither downloads it nor puts it on the classpath.
    /// </summary>
    private static void NormalizeArtifacts(JsonArray libraries)
    {
        foreach (var node in libraries)
        {
            if (node is not JsonObject lib)
                continue;

            var name = lib["name"]?.GetValue<string>() ?? "";
            var parts = name.Split(':');
            if (parts.Length < 3)
                continue;

            var group = parts[0];
            var artifact = parts[1];
            var version = parts[2];
            var classifier = parts.Length > 3 ? parts[3].Split('@')[0] : null;
            var repo = lib["url"]?.GetValue<string>() is { Length: > 0 } custom ? custom : RepoFor(group);

            var relative = $"{group.Replace('.', '/')}/{artifact}/{version}/" +
                           $"{artifact}-{version}{(string.IsNullOrEmpty(classifier) ? "" : "-" + classifier)}.jar";

            var downloads = lib["downloads"] as JsonObject ?? new JsonObject();
            if (downloads["artifact"] is JsonObject artifactNode)
            {
                // do not guess a URL when an artifact already exists: entries with an empty url under Forge are installer processors
                // locally generated products (e.g. the forge client jar); filling in the maven URL would only cause a 404.
                if (string.IsNullOrWhiteSpace(artifactNode["path"]?.GetValue<string>()))
                    artifactNode["path"] = relative;
            }
            else
            {
                downloads["artifact"] = new JsonObject
                {
                    ["path"] = relative,
                    ["url"] = repo.TrimEnd('/') + "/" + relative,
                    ["size"] = -1,
                    ["sha1"] = "",
                };
            }
            lib["downloads"] = downloads;
        }
    }

    /// <summary>Infer the Maven repository root from the groupId.</summary>
    private static string RepoFor(string group) => group switch
    {
        var g when g.StartsWith("net.minecraftforge", StringComparison.Ordinal) => "https://maven.minecraftforge.net/",
        var g when g.StartsWith("net.neoforged", StringComparison.Ordinal) => "https://maven.neoforged.net/releases/",
        var g when g.StartsWith("net.fabricmc", StringComparison.Ordinal) => "https://maven.fabricmc.net/",
        var g when g.StartsWith("org.quiltmc", StringComparison.Ordinal) => "https://maven.quiltmc.org/repository/release/",
        _ => "https://repo1.maven.org/maven2/",
    };

    /// <summary>Extract the files that need downloading from the merged result (artifacts only, no natives classifiers).</summary>
    private static List<LoaderFile> CollectFiles(JsonObject merged)
    {
        var files = new List<LoaderFile>();
        if (merged["libraries"] is not JsonArray libs)
            return files;

        foreach (var node in libs)
        {
            if (node is not JsonObject lib || lib["downloads"] is not JsonObject downloads)
                continue;
            if (downloads["artifact"] is not JsonObject artifact)
                continue;

            var path = artifact["path"]?.GetValue<string>() ?? "";
            var url = artifact["url"]?.GetValue<string>() ?? "";
            if (path.Length == 0 || url.Length == 0)
                continue;

            var dest = Path.Combine(Boot.Runtime.LibrariesDir, path.Replace('/', Path.DirectorySeparatorChar));
            var sha1 = artifact["sha1"] is { } shaNode && shaNode.GetValueKind() == JsonValueKind.String
                ? shaNode.GetValue<string>()
                : null;
            var size = long.TryParse(artifact["size"]?.ToJsonString() ?? "", out var parsed) ? parsed : -1;
            var name = lib["name"]?.GetValue<string>() ?? Path.GetFileName(path);
            files.Add(new LoaderFile("库 " + Path.GetFileName(path), url, dest, sha1, size, IsLibrary: true));
        }
        return files;
    }

    // ------------------------------------------------------------ Forge / NeoForge

    /// <summary>
    /// Forge / NeoForge: there is no standalone profile endpoint, only the installer.
    /// Download the installer → run it with Java and --installClient into the launcher root (libraries land in the shared libraries)
    /// → read the version JSON it produces (with inheritsFrom) → merge it with vanilla into this instance metadata.
    /// </summary>
    private static async Task<LoaderInstallPlan> InstallWithOfficialInstallerAsync(
        string loader, string game, string? loaderVersion,
        string vanillaJsonPath, string vanillaJarPath,
        Action<string>? step, CancellationToken ct)
    {
        var isNeo = loader.Equals("NeoForge", StringComparison.OrdinalIgnoreCase);
        var version = loaderVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            step?.Invoke($"正在获取 {loader} 版本列表…");
            version = await ResolveInstallerVersionAsync(isNeo, game, ct).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException($"没有找到适用于 {game} 的 {loader} 版本(官方可能尚未支持该游戏版本)");

        var installerUrl = string.Format(isNeo ? NeoForgeInstaller : ForgeInstaller, version);
        var cacheDir = Path.Combine(Boot.Runtime.Root, "cache", "loader-install");
        Directory.CreateDirectory(cacheDir);
        var installerPath = Path.Combine(cacheDir, Path.GetFileName(installerUrl));
        if (!File.Exists(installerPath) || new FileInfo(installerPath).Length < 64 * 1024)
        {
            step?.Invoke($"正在下载 {loader} 安装器 {version}…");
            await DownloadAsync(installerUrl, installerPath, ct).ConfigureAwait(false);
        }

        var javaMajor = RequiredJavaMajor(game);
        step?.Invoke($"正在定位 Java {javaMajor}+ …");
        var javaHome = await JavaFetch.EnsureJavaAsync(javaMajor, ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(javaHome))
            throw new InvalidOperationException($"未找到 Java {javaMajor}+,无法运行加载器安装器");
        var javaExe = Path.Combine(javaHome, "bin", "java.exe");
        if (!File.Exists(javaExe))
            javaExe = Path.Combine(javaHome, "bin", "javaw.exe");

        // the installer treats the target directory as .minecraft: libraries go straight into the shared libraries,
        // and the version JSON lands in versions\<id>, then gets moved into the instance directory.
        var installRoot = Boot.Runtime.Root;
        var profilesFile = Path.Combine(installRoot, "launcher_profiles.json");
        if (!File.Exists(profilesFile))
            await File.WriteAllTextAsync(profilesFile,
                "{\"profiles\":{},\"settings\":{},\"version\":3}", ct).ConfigureAwait(false);

        var before = SnapshotVersionDirs(installRoot);
        // the installer libraries all come from maven.neoforged.net / libraries.minecraft.net, and direct connections from China are often refused;
        // pre-fill these libraries into the shared libraries through the mirror, so the installer verifies them without downloading again.
        await PrefetchInstallerLibrariesAsync(installerPath, step, ct).ConfigureAwait(false);

        step?.Invoke($"正在运行 {loader} 安装器(会下载数百 MB 依赖,请耐心等待)…");
        await RunInstallerAsync(javaExe, installerPath, installRoot, ct).ConfigureAwait(false);

        var expected = isNeo
            ? "neoforge-" + version
            : game + "-forge-" + version.Split('-').Last();
        var generated = FindGeneratedVersionJson(installRoot, before, expected)
            ?? throw new InvalidOperationException($"{loader} 安装器未产出可用的版本 JSON(请检查日志)");

        step?.Invoke("正在合并版本元数据…");
        var profileJson = await File.ReadAllTextAsync(generated, ct).ConfigureAwait(false);
        var instanceId = $"{game}-{loader.ToLowerInvariant()}";
        var instanceDir = Path.Combine(Boot.Runtime.InstancesDir, instanceId);
        var metaDir = Path.Combine(instanceDir, "meta");
        var versionDir = Path.Combine(instanceDir, "versions");
        Directory.CreateDirectory(metaDir);
        Directory.CreateDirectory(versionDir);

        var vanillaText = await File.ReadAllTextAsync(vanillaJsonPath, ct).ConfigureAwait(false);
        var merged = Merge(vanillaText, profileJson, instanceId);
        var metaPath = Path.Combine(metaDir, instanceId + ".json");
        await File.WriteAllTextAsync(metaPath,
            merged.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), ct).ConfigureAwait(false);

        // new Forge does not produce a client jar (it reuses the vanilla one); old versions produce a patched jar
        var jarPath = Path.Combine(versionDir, instanceId + ".jar");
        var installerJar = Path.ChangeExtension(generated, ".jar");
        if (File.Exists(installerJar) && new FileInfo(installerJar).Length > 64 * 1024)
            File.Copy(installerJar, jarPath, overwrite: true);
        else
            File.Copy(vanillaJarPath, jarPath, overwrite: true);
        File.SetLastWriteTimeUtc(jarPath, DateTime.UtcNow);

        return new LoaderInstallPlan(instanceId, metaPath, jarPath, CollectFiles(merged));
    }

    /// <summary>Pick the newest loader version that fits the game version from maven-metadata.xml.</summary>
    private static async Task<string?> ResolveInstallerVersionAsync(bool isNeo, string game, CancellationToken ct)
    {
        var xml = await FetchTextAsync(isNeo ? NeoForgeMetadata : ForgeMetadata, ct).ConfigureAwait(false);
        var versions = Regex.Matches(xml, "<version>([^<]+)</version>")
            .Select(m => m.Groups[1].Value.Trim())
            .Where(v => v.Length > 0)
            .ToList();
        var comparer = Comparer<string>.Create(CompareVersions);

        if (!isNeo)
        {
            var prefix = game + "-";
            return versions
                .Where(v => v.StartsWith(prefix, StringComparison.Ordinal))
                .OrderByDescending(v => v, comparer)
                .FirstOrDefault();
        }

        // NeoForge version numbers drop the 1.x prefix: 1.21.5 → 21.5.x
        var parts = game.Split('.');
        if (parts.Length < 3)
            return null;
        var neoPrefix = $"{parts[1]}.{parts[2]}.";
        var matched = versions.Where(v => v.StartsWith(neoPrefix, StringComparison.Ordinal)).ToList();
        return matched.Where(v => !v.Contains('-')).OrderByDescending(v => v, comparer).FirstOrDefault()
               ?? matched.OrderByDescending(v => v, comparer).FirstOrDefault();
    }

    private static int CompareVersions(string? a, string? b)
    {
        var pa = (a ?? "").Split('.', '-');
        var pb = (b ?? "").Split('.', '-');
        for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
        {
            var sa = i < pa.Length ? pa[i] : "0";
            var sb = i < pb.Length ? pb[i] : "0";
            var na = int.TryParse(sa, out var x);
            var nb = int.TryParse(sb, out var y);
            var cmp = na && nb ? x.CompareTo(y) : string.CompareOrdinal(sa, sb);
            if (cmp != 0)
                return cmp;
        }
        return 0;
    }

    /// <summary>Java major version required by the installer (1.18+ → 17, 1.20.5+ → 21, earlier → 8).</summary>
    private static int RequiredJavaMajor(string game)
    {
        var parts = game.Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out var major) || !int.TryParse(parts[1], out var minor))
            return 21;
        if (major > 1 || minor >= 21)
            return 21;
        if (minor == 20 && parts.Length > 2 && int.TryParse(parts[2], out var patch) && patch >= 5)
            return 21;
        return minor >= 18 ? 17 : 8;
    }

    private static HashSet<string> SnapshotVersionDirs(string root)
    {
        var dir = Path.Combine(root, "versions");
        if (!Directory.Exists(dir))
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return Directory.EnumerateDirectories(dir)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string? FindGeneratedVersionJson(string root, HashSet<string> before, string expectedId)
    {
        var dir = Path.Combine(root, "versions");
        if (!Directory.Exists(dir))
            return null;

        var expected = Path.Combine(dir, expectedId, expectedId + ".json");
        if (File.Exists(expected))
            return expected;

        return Directory.EnumerateDirectories(dir)
            .Where(d => !before.Contains(Path.GetFileName(d)))
            .Select(d => Path.Combine(d, Path.GetFileName(d) + ".json"))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static async Task DownloadAsync(string url, string dest, CancellationToken ct)
    {
        var sources = new List<string> { url };
        if (MirrorUrl(url) is { } mirrorUrl)
            sources.Add(mirrorUrl);

        Exception? last = null;
        foreach (var source in sources)
        {
            try
            {
                await using var file = await DownloadOnceAsync(source, dest, ct).ConfigureAwait(false);
                if (file.Length < 64 * 1024)
                    throw new InvalidOperationException("响应不是有效的安装器(体积过小)");
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                Logging.AppLog.Warn("LoaderInstall", $"安装器源 {source} 失败:{ex.Message}");
            }
        }
        throw new InvalidOperationException("下载加载器安装器失败:" + last?.Message, last);
    }

    private static async Task<FileStream> DownloadOnceAsync(string url, string dest, CancellationToken ct)
    {
        using var response = await Downloader.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"HTTP {(int)response.StatusCode}");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        var file = File.Create(dest);
        try
        {
            await response.Content.CopyToAsync(file, ct).ConfigureAwait(false);
            await file.FlushAsync(ct).ConfigureAwait(false);
            return file;
        }
        catch
        {
            await file.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Fetch text: mirror first, then the official source on failure (each source retried once).</summary>
    private static async Task<string> FetchTextAsync(string url, CancellationToken ct)
    {
        var sources = new List<string>();
        if (MirrorUrl(url) is { } mirrorUrl)
            sources.Add(mirrorUrl);
        sources.Add(url);

        Exception? last = null;
        foreach (var source in sources)
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    return await Http.GetStringAsync(source, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Logging.AppLog.Warn("LoaderInstall", $"{source} 第 {attempt} 次失败:{ex.Message}");
                }
            }
        }
        throw new InvalidOperationException(
            "无法获取加载器版本列表:官方源与镜像均不可用,请检查网络后重试", last);
    }

    /// <summary>
    /// Read install_profile.json inside the installer and pre-fill the libraries it lists into the shared libraries.
    /// The installer then verifies the sha1 and reuses them directly, bypassing the blocked official maven.
    /// </summary>
    private static async Task PrefetchInstallerLibrariesAsync(string installerPath,
        Action<string>? step, CancellationToken ct)
    {
        List<(string Url, string Dest, string Sha1)> todo;
        try
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(installerPath);
            var entry = zip.GetEntry("install_profile.json");
            if (entry is null)
                return;

            todo = [];
            using var doc = await JsonDocument.ParseAsync(entry.Open(), cancellationToken: ct).ConfigureAwait(false);
            if (!doc.RootElement.TryGetProperty("libraries", out var libs)
                || libs.ValueKind != JsonValueKind.Array)
                return;

            foreach (var lib in libs.EnumerateArray())
            {
                if (!lib.TryGetProperty("downloads", out var downloads)
                    || !downloads.TryGetProperty("artifact", out var artifact))
                    continue;
                var path = artifact.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
                var url = artifact.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var sha1 = artifact.TryGetProperty("sha1", out var s) ? s.GetString() ?? "" : "";
                if (path.Length == 0 || url.Length == 0)
                    continue;

                var dest = Path.Combine(Boot.Runtime.LibrariesDir, path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(dest) && (sha1.Length == 0 || Sha1Of(dest) == sha1))
                    continue;
                todo.Add((url, dest, sha1));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // a malformed install_profile.json must not break the installer own flow
            Logging.AppLog.Warn("LoaderInstall", "读取 install_profile.json 失败:" + ex.Message);
            return;
        }

        if (todo.Count == 0)
            return;

        var done = 0;
        foreach (var (url, dest, sha1) in todo)
        {
            ct.ThrowIfCancellationRequested();
            if (!await TryFetchLibraryAsync(url, dest, sha1, ct).ConfigureAwait(false))
                throw new InvalidOperationException(
                    $"加载器依赖补全失败:{Path.GetFileName(dest)}(官方源与镜像均不可用)");

            done++;
            if (done % 5 == 0 || done == todo.Count)
                step?.Invoke($"补全安装器依赖 {done}/{todo.Count}…");
        }
    }

    /// <summary>Download a single library through the BMCLAPI mirror first, then the original URL on failure; verify sha1 after writing.</summary>
    private static async Task<bool> TryFetchLibraryAsync(string url, string dest, string sha1, CancellationToken ct)
    {
        var sources = new List<string>();
        if (MirrorUrl(url) is { } mirrorUrl)
            sources.Add(mirrorUrl);
        sources.Add(url);

        // the mirror proxy occasionally returns 5xx (upstream hiccup), so retry each source three times with a short pause
        foreach (var source in sources)
        {
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    var temp = dest + ".download";
                    await using (var file = await DownloadOnceAsync(source, temp, ct).ConfigureAwait(false))
                    {
                        if (file.Length == 0)
                            continue;
                    }

                    if (sha1.Length > 0 && Sha1Of(temp) != sha1)
                    {
                        File.Delete(temp);
                        continue;
                    }
                    File.Move(temp, dest, overwrite: true);
                    return true;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logging.AppLog.Warn("LoaderInstall", $"库 {source} 第 {attempt} 次预取失败:{ex.Message}");
                    if (attempt < 3)
                        await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
                }
            }
        }
        return false;
    }

    private static string Sha1Of(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = System.Security.Cryptography.SHA1.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    private static async Task RunInstallerAsync(string javaExe, string installer, string installRoot,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = javaExe,
            WorkingDirectory = installRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        psi.ArgumentList.Add("-jar");
        psi.ArgumentList.Add(installer);
        psi.ArgumentList.Add("--installClient");
        psi.ArgumentList.Add(installRoot);

        using var process = new Process { StartInfo = psi };
        var tail = new Queue<string>();
        void Tap(string? line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;
            Logging.AppLog.Info("LoaderInstall", line);
            lock (tail)
            {
                tail.Enqueue(line.Trim());
                while (tail.Count > 12)
                    tail.Dequeue();
            }
        }

        process.OutputDataReceived += (_, e) => Tap(e.Data);
        process.ErrorDataReceived += (_, e) => Tap(e.Data);
        if (!process.Start())
            throw new InvalidOperationException("无法启动加载器安装器进程");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new InvalidOperationException("加载器安装器 20 分钟内未完成,已中止");
        }

        if (process.ExitCode != 0)
        {
            string detail;
            lock (tail)
                detail = string.Join(" | ", tail);
            throw new InvalidOperationException(
                $"加载器安装器返回 {process.ExitCode}:{detail}");
        }
    }
}
