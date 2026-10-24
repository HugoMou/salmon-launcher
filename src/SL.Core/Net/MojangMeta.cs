using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace SL.Core.Net;

/// <summary>
/// Mojang 官方元数据接入(阶段二):
/// - version_manifest_v2:版本清单(带缓存,30 分钟新鲜度,离线时回退旧缓存);
/// - 版本 JSON:解析 client / libraries / assetIndex,并有 windows 平台 rules 过滤。
/// </summary>
public static class MojangMeta
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(30);

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    // ============================== 模型 ==============================

    public sealed record VersionEntry(string Id, string Type, string Url, string? Sha1, DateTimeOffset ReleaseTime)
    {
        public string Badge => Type switch
        {
            "release" => "稳定版",
            "snapshot" => "快照",
            "old_beta" => "Beta",
            "old_alpha" => "Alpha",
            _ => Type,
        };
    }

    public sealed record Artifact(string Url, long Size, string Sha1);

    public sealed record LibraryArtifact(string Name, string RelativePath, string Url, long Size, string Sha1, bool IsNative);

    public sealed record VersionDetail
    {
        public required string Id { get; init; }
        public Artifact? Client { get; init; }
        public Artifact? AssetIndex { get; init; }
        public string AssetId { get; init; } = "legacy";
        public required IReadOnlyList<LibraryArtifact> Libraries { get; init; }
        public string MainClass { get; init; } = "";
        public string Type { get; init; } = "release";
        /// <summary>javaVersion.majorVersion(缺失视为 8)。</summary>
        public int JavaMajor { get; init; } = 8;
        public long LibraryBytes => Libraries.Sum(l => l.Size);
    }

    // ============================== 版本清单 ==============================

    /// <summary>取版本清单:缓存优先后网络;网络失败回退过期缓存;两者皆无 → 抛出。</summary>
    public static async Task<IReadOnlyList<VersionEntry>> GetVersionsAsync(bool forceRefresh, CancellationToken cancellation)
    {
        var cacheFile = Path.Combine(Core.Boot.Runtime.MetaDir, "version_manifest_v2.json");
        var stampFile = Path.Combine(Core.Boot.Runtime.MetaDir, "version_manifest.stamp");
        var fresh = File.Exists(stampFile)
                    && DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(stampFile) < Freshness;

        // 读缓存路径(新鲜缓存始终可用;非强制刷新时旧缓存也可用)
        if (File.Exists(cacheFile) && (fresh || !forceRefresh))
        {
            try
            {
                return ParseManifest(await File.ReadAllTextAsync(cacheFile, cancellation).ConfigureAwait(false));
            }
            catch
            {
                // 缓存损坏 → 走网络
            }
        }

        try
        {
            var json = await FetchAsync(ManifestUrl, cancellation).ConfigureAwait(false);
            ParseManifest(json); // 预检,坏响应不落缓存
            Directory.CreateDirectory(Core.Boot.Runtime.MetaDir);
            await File.WriteAllTextAsync(cacheFile, json, cancellation).ConfigureAwait(false);
            File.WriteAllText(stampFile, DateTimeOffset.UtcNow.ToString("O"));
            return ParseManifest(json);
        }
        catch (Exception)
        {
            // 网络失败:有过期缓存就降级使用
            if (File.Exists(cacheFile))
                return ParseManifest(await File.ReadAllTextAsync(cacheFile, cancellation).ConfigureAwait(false));
            throw;
        }
    }

    private static IReadOnlyList<VersionEntry> ParseManifest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var versions = doc.RootElement.GetProperty("versions")
            .EnumerateArray()
            .Select(v => new VersionEntry(
                v.GetProperty("id").GetString() ?? "",
                v.GetProperty("type").GetString() ?? "",
                v.GetProperty("url").GetString() ?? "",
                v.TryGetProperty("sha1", out var sha) ? sha.GetString() : null,
                v.TryGetProperty("releaseTime", out var t) && t.TryGetDateTimeOffset(out var time) ? time : default))
            .Where(e => e.Url.Length > 0)
            .ToArray();
        return versions;
    }

    // ============================== 版本详情 ==============================

    /// <summary>下载并解析版本 JSON(client / libraries / assetIndex,按 Windows 平台过滤)。</summary>
    public static async Task<VersionDetail?> GetVersionAsync(string versionUrl, CancellationToken cancellation)
    {
        var json = await FetchAsync(versionUrl, cancellation).ConfigureAwait(false);
        return ParseVersionJson(json);
    }

    /// <summary>解析版本 JSON 文本(启动流水线直接吃 instances/<id>/meta/<id>.json)。</summary>
    public static VersionDetail ParseVersionJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
 
        var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
        var mainClass = root.TryGetProperty("mainClass", out var mc) ? mc.GetString() ?? "" : "";
        var type = root.TryGetProperty("type", out var tp) ? tp.GetString() ?? "release" : "release";
        var javaMajor = 8;
        if (root.TryGetProperty("javaVersion", out var jv)
            && jv.ValueKind == JsonValueKind.Object
            && jv.TryGetProperty("majorVersion", out var mj)
            && mj.TryGetInt32(out var majors))
            javaMajor = majors;

        var detail = new VersionDetail
        {
            Id = id,
            Libraries = ParseLibraries(root),
            MainClass = mainClass,
            Type = type,
            JavaMajor = javaMajor,
        };

        if (root.TryGetProperty("downloads", out var downloads))
        {
            if (downloads.TryGetProperty("client", out var client))
                detail = detail with { Client = ReadArtifact(client) };

            if (downloads.TryGetProperty("client_mappings", out var mappings)
                && mappings.ValueKind == JsonValueKind.Object)
            {
                // 客户端映射表(混淆表)阶段三启动流程才需要,这里忽略
            }
        }

        if (root.TryGetProperty("assetIndex", out var assetIndex) && assetIndex.ValueKind == JsonValueKind.Object)
        {
            detail = detail with
            {
                AssetIndex = ReadArtifact(assetIndex),
                AssetId = assetIndex.TryGetProperty("id", out var aid) ? aid.GetString() ?? "legacy" : "legacy",
            };
        }

        return detail;
    }

    private static Artifact ReadArtifact(JsonElement obj) => new(
        obj.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
        obj.TryGetProperty("size", out var s) && s.TryGetInt64(out var size) ? size : -1,
        obj.TryGetProperty("sha1", out var sh) ? sh.GetString() ?? "" : "");

    private static IReadOnlyList<LibraryArtifact> ParseLibraries(JsonElement root)
    {
        var result = new List<LibraryArtifact>();
        if (!root.TryGetProperty("libraries", out var libraries) || libraries.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var lib in libraries.EnumerateArray())
        {
            if (!EvaluateRules(lib))
                continue;

            var name = lib.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";

            // 2026 新格式:natives 直接编码在坐标里(…:natives-windows)。
            // 其他平台的 natives 变体(…-natives-linux/macos/…)在本机完全不适用 → 跳过。
            if (IsForeignNativeName(name))
                continue;
            var cppNative = IsWindowsNativeName(name);

            if (lib.TryGetProperty("downloads", out var dl) && dl.ValueKind == JsonValueKind.Object)
            {
                if (dl.TryGetProperty("artifact", out var artifact) && artifact.ValueKind == JsonValueKind.Object)
                {
                    var a = ReadArtifact(artifact);
                    if (a.Url.Length > 0)
                        result.Add(new LibraryArtifact(
                            name,
                            artifact.TryGetProperty("path", out var p) ? p.GetString() ?? "" : DerivePath(name),
                            a.Url, a.Size, a.Sha1, IsNative: cppNative));
                }

                // 旧格式(有 classifiers 时):Windows natives 单独拉取(阶段三启动准备时解压)
                if (dl.TryGetProperty("classifiers", out var classifiers)
                    && classifiers.ValueKind == JsonValueKind.Object
                    && classifiers.TryGetProperty("natives-windows", out var natives)
                    && natives.ValueKind == JsonValueKind.Object)
                {
                    var a = ReadArtifact(natives);
                    if (a.Url.Length > 0)
                        result.Add(new LibraryArtifact(
                            name,
                            natives.TryGetProperty("path", out var p) ? p.GetString() ?? "" : DerivePath(name),
                            a.Url, a.Size, a.Sha1, IsNative: true));
                }
            }
        }
        return result;
    }

    /// <summary>2026 新格式:Windows natives 以坐标后缀 natives-windows 表示(x64 主线)。</summary>
    private static bool IsWindowsNativeName(string name)
        => name.EndsWith("natives-windows", StringComparison.Ordinal);

    /// <summary>其他平台(含 Windows ARM64)的 natives 变体 → x64 主线不可用。</summary>
    private static bool IsForeignNativeName(string name)
        => name.Contains("natives-linux", StringComparison.Ordinal)
           || name.Contains("natives-macos", StringComparison.Ordinal)
           || name.Contains("natives-freebsd", StringComparison.Ordinal)
           || name.Contains("natives-openbsd", StringComparison.Ordinal)
           || name.Contains("natives-solaris", StringComparison.Ordinal)
           || name.EndsWith("natives-windows-arm64", StringComparison.Ordinal);

    /// <summary>Mojang library rules 的 Windows 平台判定:rules 为空 → 允许;否则按逐条 allow/deny 半径收敛。</summary>
    private static bool EvaluateRules(JsonElement lib)
    {
        if (!lib.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
            return true;

        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var action = rule.TryGetProperty("action", out var a) && a.GetString() == "deny" ? "deny" : "allow";

            // 当前仅支持无 feature 的 os 规则;含 feature 的规则视为不适用(不收敛)
            bool applies = true;
            if (rule.TryGetProperty("os", out var os) && os.ValueKind == JsonValueKind.Object)
            {
                var osName = os.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                applies = osName is "windows" or "";
                if (os.TryGetProperty("arch", out var arch))
                    applies &= arch.GetString() is null or "" or "x64";
            }
            if (rule.TryGetProperty("features", out _))
                applies = false; // TLA/特殊客户端特性:阶段三按需启用

            if (applies)
                allowed = action == "allow";
        }
        return allowed;
    }

    /// <summary>被解析库相对路径的兜底推导:group/artifact/version/name.jar。</summary>
    private static string DerivePath(string name)
    {
        var parts = name.Split(':');
        return parts.Length switch
        {
            >= 3 => $"{parts[0].Replace('.', '/')}/{parts[1]}/{parts[2]}/{parts[1]}-{parts[2]}{(parts.Length > 3 ? "-" + parts[3].Split('@')[0] : "")}.jar",
            _ => $"{name.Replace(':', '/')}.jar",
        };
    }

    private static async Task<string> FetchAsync(string url, CancellationToken cancellation)
    {
        using var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellation)
            .ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK)
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}:{url}");
        return await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
    }
}
