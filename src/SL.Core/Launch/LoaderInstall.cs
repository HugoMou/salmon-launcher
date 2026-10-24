using System.Text.Json;
using System.Text.Json.Nodes;

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
/// Fabric / Quilt 一键安装:拉取官方 profile JSON,与原版版本 JSON 合并成
/// 一份可直接启动的实例元数据,并列出需要补齐的库文件。
/// </summary>
public static class LoaderInstall
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private const string FabricLoaderList = "https://meta.fabricmc.net/v2/versions/loader";
    private const string FabricProfile = "https://meta.fabricmc.net/v2/versions/loader/{0}/{1}/profile/json";
    private const string QuiltLoaderList = "https://meta.quiltmc.org/v3/versions/loader";
    private const string QuiltProfile = "https://meta.quiltmc.org/v3/versions/loader/{0}/{1}/profile/json";

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
        CancellationToken ct)
    {
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
            File.Copy(vanillaJarPath, jarPath, overwrite: true);

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
            ["arguments"] = MergeArguments(vanilla["arguments"], profile["arguments"]),
            ["libraries"] = MergeLibraries(vanilla["libraries"], profile["libraries"]),
        };
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
    /// Fabric / Quilt 的 profile 只给 maven 坐标(name + 仓库根 url),没有 downloads.artifact。
    /// 这里按坐标补出 artifact(path/url),否则启动器既不会下载也放不进 classpath。
    /// </summary>
    private static void NormalizeArtifacts(JsonArray libraries)
    {
        foreach (var node in libraries)
        {
            if (node is not JsonObject lib)
                continue;
            if (lib["downloads"] is JsonObject existing && existing["artifact"] is JsonObject)
                continue;

            var name = lib["name"]?.GetValue<string>() ?? "";
            var parts = name.Split(':');
            if (parts.Length < 3)
                continue;

            var group = parts[0];
            var artifact = parts[1];
            var version = parts[2];
            var classifier = parts.Length > 3 ? parts[3] : null;
            // 未写仓库根的库默认来自 Maven Central(Fabric profile 对第三方库就是如此)
            var repo = lib["url"]?.GetValue<string>() is { Length: > 0 } custom
                ? custom
                : "https://repo1.maven.org/maven2/";

            var relative = $"{group.Replace('.', '/')}/{artifact}/{version}/" +
                           $"{artifact}-{version}{(classifier is null ? "" : "-" + classifier)}.jar";
            var url = repo.TrimEnd('/') + "/" + relative;

            var downloads = lib["downloads"] as JsonObject ?? new JsonObject();
            downloads["artifact"] = new JsonObject
            {
                ["path"] = relative,
                ["url"] = url,
                ["size"] = -1,
                ["sha1"] = "",
            };
            lib["downloads"] = downloads;
        }
    }

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
}
