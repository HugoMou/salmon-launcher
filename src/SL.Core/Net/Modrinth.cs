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
using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace SL.Core.Net;

/// <summary>Modrinth resource category: determines the search facets and the install target directory.</summary>
public enum ModrinthCategory
{
    /// <summary>Mods (need game_versions + loader filtering, installed into mods).</summary>
    Mods,
    /// <summary>Shader packs (project_type:shader, generic Iris shaders, installed into shaderpacks).</summary>
    Shaders,
    /// <summary>Resource packs (project_type:resourcepack, installed into resourcepacks).</summary>
    ResourcePacks,
}

/// <summary>Stage 4: Modrinth v2 search (the mod repository page). Every call is real HTTP.</summary>
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
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SalmonLauncher/0.3 (SalmonLauncher Launcher)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    /// <summary>Mirror first (for networks in China). Driven by the ModrinthMirror setting.</summary>
    public static bool PreferMirror { get; set; } = true;

    private const string OfficialApi = "https://api.modrinth.com/v2";
    private const string MirrorApi = "https://mod.mcimirror.top/modrinth/v2";
    private const string OfficialCdn = "https://cdn.modrinth.com";
    private const string MirrorCdn = "https://mod.mcimirror.top";

    private static IEnumerable<string> ApiOrder()
        => PreferMirror ? [MirrorApi, OfficialApi] : [OfficialApi, MirrorApi];

    /// <summary>Try the official API and the mirror in the current preference order; return as soon as one succeeds.</summary>
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

    /// <summary>A search result row.</summary>
    /// <param name="Slug">Project identifier (versions are queried later when downloading a file)</param>
    /// <param name="Title">Display name</param>
    /// <param name="Description">One-sentence summary</param>
    /// <param name="Author"></param>
    /// <param name="DownloadsText">Download count (pre-formatted)</param>
    /// <param name="Categories">First three category tags</param>
    public sealed record ProjectHit(string Slug, string Title, string Description, string Author,
        string DownloadsText, IReadOnlyList<string> Categories);

    /// <summary>Latest available file of a project for {gameVersion, loader}.</summary>
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
        // Modrinth facets: a two-level nested array [["versions:26.3"],["categories:fabric"]] (AND within a group, OR between groups)
        var facets = $"[[\"versions:{gameVersion}\"],[\"categories:{loader}\"]]";
        return SearchByFacetsAsync(query, facets, cancellation);
    }

    /// <summary>Search with custom facets (shaders/resource packs pass ["project_type:..."], without game_versions filtering).</summary>
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

    /// <summary>Latest primary file of {slug} (no version/loader filtering — used for generic resources such as shaders and resource packs).</summary>
    public static async Task<VersionFile?> LatestVersionFileAsync(string slug, CancellationToken cancellation)
    {
        var url = $"/project/{Uri.EscapeDataString(slug)}/version";
        using var doc = await GetJsonAsync(url, cancellation).ConfigureAwait(false);
        return FirstPrimaryFile(doc);
    }

    /// <summary>Latest primary file of {slug} for the given version/loader.</summary>
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
            // when this level has no primary file, keep looking at the next version
        }
        return null;
    }

    /// <summary>Mirror URL of the Modrinth CDN (used as the second download source); returns null for non-CDN links.</summary>
    public static string? MirrorOf(string url)
        => url.StartsWith(OfficialCdn + "/", StringComparison.OrdinalIgnoreCase)
            ? MirrorCdn + url[OfficialCdn.Length..]
            : null;
}
