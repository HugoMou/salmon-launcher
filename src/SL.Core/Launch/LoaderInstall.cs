using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace SL.Core.Launch;

/// <summary>加载器安装结果:实例 id + 待下载文件清单。</summary>
public sealed record LoaderInstallPlan(
    string InstanceId,
    string MetaPath,
    string JarPath,
    IReadOnlyList<LoaderFile> Files);

/// <summary>加载器安装需要补齐的一个文件。</summary>
public sealed record LoaderFile(string DisplayName, string Url, string DestPath, string? Sha1, long Size, bool IsLibrary);

/// <summary>
/// 加载器一键安装:
/// - Fabric / Quilt:拉取官方 profile JSON,与原版版本 JSON 合并成可直接启动的实例元数据;
/// - Forge / NeoForge:下载官方 installer 并以 --installClient 就地安装,再合并其产出的版本 JSON。
/// 两条路径最终都输出「实例元数据 + 核心 jar + 待补齐库清单」。
/// </summary>
public static class LoaderInstall
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly HttpClient Downloader = new() { Timeout = TimeSpan.FromMinutes(10) };

    private const string FabricLoaderList = "https://meta.fabricmc.net/v2/versions/loader";
    private const string FabricProfile = "https://meta.fabricmc.net/v2/versions/loader/{0}/{1}/profile/json";
    private const string QuiltLoaderList = "https://meta.quiltmc.org/v3/versions/loader";
    private const string QuiltProfile = "https://meta.quiltmc.org/v3/versions/loader/{0}/{1}/profile/json";
    private const string ForgeMetadata = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
    private const string ForgeInstaller = "https://maven.minecraftforge.net/net/minecraftforge/forge/{0}/forge-{0}-installer.jar";
    private const string NeoForgeMetadata = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
    private const string NeoForgeInstaller = "https://maven.neoforged.net/releases/net/neoforged/neoforge/{0}/neoforge-{0}-installer.jar";

    /// <summary>BMCLAPI 的 maven 代理(同时覆盖 Forge / NeoForge / Mojang 库)。</summary>
    private const string BmclMaven = "https://bmclapi2.bangbang93.com/maven";

    /// <summary>Forge / NeoForge maven 地址 → BMCLAPI 代理;其它地址返回 null。</summary>
    private static string? MirrorUrl(string url)
    {
        if (url.Contains("maven.minecraftforge.net/", StringComparison.Ordinal))
            return url.Replace("https://maven.minecraftforge.net/", BmclMaven + "/", StringComparison.Ordinal);
        if (url.Contains("maven.neoforged.net/releases/", StringComparison.Ordinal))
            return url.Replace("https://maven.neoforged.net/releases/", BmclMaven + "/", StringComparison.Ordinal);
        return null;
    }

    /// <summary>取加载器最新版本号(取不到返回 null)。</summary>
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
            // 没有标记 stable 的:取第一条
            if (doc.RootElement.GetArrayLength() > 0
                && doc.RootElement[0].TryGetProperty("version", out var first))
                return first.GetString();
        }
        catch
        {
            // 交给上层提示
        }
        return null;
    }

    /// <summary>
    /// 生成合并后的实例(元数据 + 核心 jar 副本),返回待下载清单。
    /// vanillaJson / vanillaJar 指向已安装的原版实例。
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

        // 加载器没有自己的核心 jar,复制原版 jar 作为实例核心
        var jarPath = Path.Combine(versionDir, instanceId + ".jar");
        if (!File.Exists(jarPath))
        {
            File.Copy(vanillaJarPath, jarPath, overwrite: true);
            // 复制会沿用原版 jar 的时间戳,这里改成当前时间,否则新实例不会排在实例列表最前
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

        // 新格式(arguments.jvm/game)与原版合并;老版本 Forge 只有 minecraftArguments,原样带上
        var mergedArgs = MergeArguments(vanilla["arguments"], profile["arguments"]);
        if (mergedArgs["jvm"]!.AsArray().Count > 0 || mergedArgs["game"]!.AsArray().Count > 0)
            merged["arguments"] = mergedArgs;
        else if (profile["minecraftArguments"] is { } legacyArgs)
            merged["minecraftArguments"] = legacyArgs.DeepClone();

        return merged;
    }

    /// <summary>加载器参数在前,原版参数在后;两边都是对象形式的 { jvm:[], game:[] }。</summary>
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

    /// <summary>加载器库在前(同名覆盖原版),原版库在后;按 name 去重。</summary>
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
            // 按 group:artifact[:classifier] 去重:同一依赖只留加载器指定的版本,
            // 否则原版自带的 asm 9.6 会和加载器的 9.10.1 同时在 classpath,Fabric 会拒绝启动。
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

    /// <summary>maven 坐标的依赖键(忽略版本,保留 classifier)。</summary>
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
    /// Fabric / Quilt 的 profile 只给 maven 坐标(name + 仓库根 url),没有 downloads.artifact;
    /// Forge 产出的元数据里也会出现 artifact 缺 url 的条目。
    /// 这里统一按坐标补出 artifact(path/url),否则启动器既不会下载也放不进 classpath。
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
                // 已有 artifact 就不猜 URL:Forge 里 url 为空的条目是安装器 processors
                // 本地生成的产物(如 forge client jar),填 maven 地址反而会 404。
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

    /// <summary>按 groupId 推断 Maven 仓库根。</summary>
    private static string RepoFor(string group) => group switch
    {
        var g when g.StartsWith("net.minecraftforge", StringComparison.Ordinal) => "https://maven.minecraftforge.net/",
        var g when g.StartsWith("net.neoforged", StringComparison.Ordinal) => "https://maven.neoforged.net/releases/",
        var g when g.StartsWith("net.fabricmc", StringComparison.Ordinal) => "https://maven.fabricmc.net/",
        var g when g.StartsWith("org.quiltmc", StringComparison.Ordinal) => "https://maven.quiltmc.org/repository/release/",
        _ => "https://repo1.maven.org/maven2/",
    };

    /// <summary>从合并结果里取出需要下载的文件(仅取 artifact,不含 natives 分类器)。</summary>
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
    /// Forge / NeoForge:官方没有独立 profile 接口,只能靠 installer。
    /// 下载 installer → 用 Java 以 --installClient 装进启动器根目录(库落到共享 libraries)
    /// → 读取它产出的版本 JSON(带 inheritsFrom)→ 与原版合并成本实例元数据。
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

        // 安装器把目标目录当成 .minecraft:库直接落进共享 libraries,
        // 版本 JSON 落在 versions\<id>,随后再搬到实例目录。
        var installRoot = Boot.Runtime.Root;
        var profilesFile = Path.Combine(installRoot, "launcher_profiles.json");
        if (!File.Exists(profilesFile))
            await File.WriteAllTextAsync(profilesFile,
                "{\"profiles\":{},\"settings\":{},\"version\":3}", ct).ConfigureAwait(false);

        var before = SnapshotVersionDirs(installRoot);
        // 官方 maven 不通时(国内常见)让安装器改走 BMCLAPI 代理,否则库下载会全部失败
        var mirror = await MavenUnreachableAsync(isNeo, ct).ConfigureAwait(false) ? BmclMaven : null;
        step?.Invoke(mirror is null
            ? $"正在运行 {loader} 安装器(会下载数百 MB 依赖,请耐心等待)…"
            : $"正在运行 {loader} 安装器(官方源不可达,已切换 BMCLAPI 镜像)…");
        await RunInstallerAsync(javaExe, installerPath, installRoot, mirror, ct).ConfigureAwait(false);

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

        // 新版 Forge 不产出客户端 jar(沿用原版);老版本会产出 patch 过的 jar
        var jarPath = Path.Combine(versionDir, instanceId + ".jar");
        var installerJar = Path.ChangeExtension(generated, ".jar");
        if (File.Exists(installerJar) && new FileInfo(installerJar).Length > 64 * 1024)
            File.Copy(installerJar, jarPath, overwrite: true);
        else
            File.Copy(vanillaJarPath, jarPath, overwrite: true);
        File.SetLastWriteTimeUtc(jarPath, DateTime.UtcNow);

        return new LoaderInstallPlan(instanceId, metaPath, jarPath, CollectFiles(merged));
    }

    /// <summary>从 maven-metadata.xml 里挑出适配该游戏版本的最新加载器版本。</summary>
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

        // NeoForge 的版本号去掉 1.x 前缀:1.21.5 → 21.5.x
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

    /// <summary>安装器所需 Java 主版本(1.18+ → 17,1.20.5+ → 21,更早 → 8)。</summary>
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

    /// <summary>取文本:主源失败时回退 BMCLAPI 镜像。</summary>
    private static async Task<string> FetchTextAsync(string url, CancellationToken ct)
    {
        try
        {
            return await Http.GetStringAsync(url, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (MirrorUrl(url) is not { } mirrorUrl)
                throw;
            Logging.AppLog.Warn("LoaderInstall", $"主源 {url} 不可用({ex.Message}),改用镜像");
            return await Http.GetStringAsync(mirrorUrl, ct).ConfigureAwait(false);
        }
    }

    /// <summary>探测官方 maven 是否可达(8 秒内无响应即判定不可达)。</summary>
    private static async Task<bool> MavenUnreachableAsync(bool isNeo, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var host = isNeo ? "https://maven.neoforged.net/releases/" : "https://maven.minecraftforge.net/";
            using var response = await Http.GetAsync(host, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                .ConfigureAwait(false);
            return false; // 有响应即视为可达(404 也算通)
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return true;
        }
    }

    private static async Task RunInstallerAsync(string javaExe, string installer, string installRoot,
        string? mirror, CancellationToken ct)
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
        if (!string.IsNullOrEmpty(mirror))
        {
            psi.ArgumentList.Add("--mirror");
            psi.ArgumentList.Add(mirror);
        }

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
