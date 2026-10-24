using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace SL.Core.Net;

/// <summary>Modrinth 资源分类:决定检索 facets 与安装目标目录。</summary>
public enum ModrinthCategory
{
    /// <summary>模组(需 game_versions + loader 过滤,装到 mods)。</summary>
    Mods,
    /// <summary>光影包(project_type:shader,通用 Iris 光影,装到 shaderpacks)。</summary>
    Shaders,
    /// <summary>资源包(project_type:resourcepack,装到 resourcepacks)。</summary>
    ResourcePacks,
}

/// <summary>阶段四:Modrinth v2 检索(模组仓库页)。全部为真实 HTTP 调用。</summary>
public static class Modrinth
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SalmonLauncher/0.3 (Salmon Launcher Launcher)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    /// <summary>镜像优先(国内网络)。由设置项 ModrinthMirror 驱动。</summary>
    public static bool PreferMirror { get; set; } = true;

    private const string OfficialApi = "https://api.modrinth.com/v2";
    private const string MirrorApi = "https://mod.mcimirror.top/modrinth/v2";
    private const string OfficialCdn = "https://cdn.modrinth.com";
    private const string MirrorCdn = "https://mod.mcimirror.top";

    private static IEnumerable<string> ApiOrder()
        => PreferMirror ? [MirrorApi, OfficialApi] : [OfficialApi, MirrorApi];

    /// <summary>按当前偏好顺序尝试官方接口与镜像,任一成功即返回。</summary>
    private static async Task<JsonDocument> GetJsonAsync(string pathAndQuery, CancellationToken cancellation)
    {
        Exception? last = null;
        foreach (var baseUrl in ApiOrder())
        {
            try
            {
                var text = await Client.GetStringAsync(baseUrl + pathAndQuery, cancellation).ConfigureAwait(false);
                return JsonDocument.Parse(text);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                Logging.AppLog.Warn("Modrinth", $"{baseUrl} 不可用:{ex.Message}");
            }
        }
        throw new InvalidOperationException("Modrinth 接口不可达(网络受限时可切换镜像或稍后重试)", last);
    }

    /// <summary>搜索结果行。</summary>
    /// <param name="Slug">项目标识(下载文件时再查 versions)</param>
    /// <param name="Title">显示名</param>
    /// <param name="Description">一句话简介</param>
    /// <param name="Author"></param>
    /// <param name="DownloadsText">下载量(已格式化)</param>
    /// <param name="Categories">前三个分类 tag</param>
    public sealed record ProjectHit(string Slug, string Title, string Description, string Author,
        string DownloadsText, IReadOnlyList<string> Categories);

    /// <summary>某项目针对 {gameVersion, loader} 的最新可用文件。</summary>
    public sealed record VersionFile(string FileName, string Url, long Size, string? Sha1)
    {
        public string SizeText => Size >= 1 << 20 ? $"{Size / (1024.0 * 1024.0):F1} MB" : $"{Size / 1024.0:F0} KB";
    }

    /// <summary>
    /// facets:game_versions=["26.3"],categories=["fabric"]
    /// </summary>
    public static Task<IReadOnlyList<ProjectHit>> SearchAsync(
        string query, string gameVersion, string loader, CancellationToken cancellation)
    {
        // Modrinth facets:两层嵌套数组 [["versions:26.3"],["categories:fabric"]](组内 AND,组间 OR)
        var facets = $"[[\"versions:{gameVersion}\"],[\"categories:{loader}\"]]";
        return SearchByFacetsAsync(query, facets, cancellation);
    }

    /// <summary>按自定义 facets 检索(光影/资源包传 ["project_type:..."],不过滤 game_versions)。</summary>
    public static async Task<IReadOnlyList<ProjectHit>> SearchByFacetsAsync(
        string query, string facets, CancellationToken cancellation)
    {
        var url = "/search?limit=20&index=relevance&display=irrelevant"
                  + $"&query={Uri.EscapeDataString(query)}&facets={Uri.EscapeDataString(facets)}";

        using var doc = await GetJsonAsync(url, cancellation).ConfigureAwait(false);
        var list = new List<ProjectHit>();
        foreach (var hit in doc.RootElement.GetProperty("hits").EnumerateArray())
        {
            var cats = new List<string>();
            foreach (var c in hit.GetProperty("categories").EnumerateArray())
            {
                var s = c.GetString();
                if (s is not null && cats.Count < 3)
                    cats.Add(s);
            }

            var downloads = hit.TryGetProperty("downloads", out var d) ? d.GetInt64() : 0;
            list.Add(new ProjectHit(
                hit.TryGetProperty("slug", out var sl) ? sl.GetString() ?? "" : "",
                hit.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
                hit.TryGetProperty("description", out var de) ? de.GetString() ?? "" : "",
                hit.TryGetProperty("author", out var au) ? au.GetString() ?? "" : "",
                downloads >= 1_000_000 ? $"{downloads / 1_000_000.0:F1}M"
                    : downloads >= 1_000 ? $"{downloads / 1_000.0:F1}k"
                    : downloads.ToString(),
                cats));
        }
        return list;
    }

    /// <summary>取 {slug} 的最新主文件(不过滤版本/加载器 —— 光影、资源包等通用资源使用)。</summary>
    public static async Task<VersionFile?> LatestVersionFileAsync(string slug, CancellationToken cancellation)
    {
        var url = $"/project/{Uri.EscapeDataString(slug)}/version";
        using var doc = await GetJsonAsync(url, cancellation).ConfigureAwait(false);
        return FirstPrimaryFile(doc);
    }

    /// <summary>取 {slug} 在指定版本/加载器下的最新主文件。</summary>
    public static async Task<VersionFile?> LatestVersionFileAsync(
        string slug, string gameVersion, string loader, CancellationToken cancellation)
    {
        var url = $"/project/{Uri.EscapeDataString(slug)}/version"
                  + $"?game_versions=[\"{Uri.EscapeDataString(gameVersion)}\"]&loaders=[\"{Uri.EscapeDataString(loader)}\"]";
        using var doc = await GetJsonAsync(url, cancellation).ConfigureAwait(false);
        return FirstPrimaryFile(doc);
    }

    private static VersionFile? FirstPrimaryFile(JsonDocument doc)
    {
        foreach (var version in doc.RootElement.EnumerateArray())
        {
            if (!version.TryGetProperty("files", out var files))
                continue;
            foreach (var f in files.EnumerateArray())
            {
                var prim = f.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True;
                if (!prim)
                    continue;
                return new VersionFile(
                    f.TryGetProperty("filename", out var fn) ? fn.GetString() ?? "" : "",
                    f.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                    f.TryGetProperty("size", out var sz) && sz.TryGetInt64(out var size) ? size : -1,
                    f.TryGetProperty("hashes", out var hs) && hs.TryGetProperty("sha1", out var sh) ? sh.GetString() : null);
            }
            // 这一级没有 primary 文件就继续找下一个版本
        }
        return null;
    }

    /// <summary>Modrinth CDN 的镜像地址(下载时作为第二来源);非 CDN 链接返回 null。</summary>
    public static string? MirrorOf(string url)
        => url.StartsWith(OfficialCdn + "/", StringComparison.OrdinalIgnoreCase)
            ? MirrorCdn + url[OfficialCdn.Length..]
            : null;
}
