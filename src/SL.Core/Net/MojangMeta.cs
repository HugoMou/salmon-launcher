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

/// <summary>
/// Mojang official metadata access (stage 2):
/// - version_manifest_v2: version manifest (cached, 30-minute freshness, falls back to the stale cache when offline);
/// - version JSON: parse client / libraries / assetIndex, with Windows platform rules filtering.
/// </summary>
public static class MojangMeta
{
    private const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(30);

    private static readonly HttpClient Client = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
    };

    // ============================== Models ==============================

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
        /// <summary>javaVersion.majorVersion (treated as 8 when absent).</summary>
        public int JavaMajor { get; init; } = 8;
        public long LibraryBytes => Libraries.Sum(l => l.Size);
    }

    // ============================== Version manifest ==============================

    /// <summary>Get the version manifest: cache first, then network; fall back to the stale cache when the network fails; throw when neither exists.</summary>
    public static async Task<IReadOnlyList<VersionEntry>> GetVersionsAsync(bool forceRefresh, CancellationToken cancellation)
    {
        var cacheFile = Path.Combine(Core.Boot.Runtime.MetaDir, "version_manifest_v2.json");
        var stampFile = Path.Combine(Core.Boot.Runtime.MetaDir, "version_manifest.stamp");
        var fresh = File.Exists(stampFile)
                    && DateTimeOffset.UtcNow - File.GetLastWriteTimeUtc(stampFile) < Freshness;

        // read the cached path (a fresh cache is always usable; a stale one is usable unless a forced refresh is requested)
        if (File.Exists(cacheFile) && (fresh || !forceRefresh))
        {
            try
            {
                return ParseManifest(await File.ReadAllTextAsync(cacheFile, cancellation).ConfigureAwait(false));
            }
            catch
            {
                // cache corrupted → go to the network
            }
        }

        try
        {
            var json = await FetchAsync(ManifestUrl, cancellation).ConfigureAwait(false);
            ParseManifest(json); // pre-validate so a bad response is not cached
            Directory.CreateDirectory(Core.Boot.Runtime.MetaDir);
            await File.WriteAllTextAsync(cacheFile, json, cancellation).ConfigureAwait(false);
            File.WriteAllText(stampFile, DateTimeOffset.UtcNow.ToString("O"));
            return ParseManifest(json);
        }
        catch (Exception)
        {
            // network failed: degrade to the stale cache when one exists
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

    // ============================== Version details ==============================

    /// <summary>Download and parse the version JSON (client / libraries / assetIndex, filtered by Windows platform).</summary>
    public static async Task<VersionDetail?> GetVersionAsync(string versionUrl, CancellationToken cancellation)
    {
        var json = await FetchAsync(versionUrl, cancellation).ConfigureAwait(false);
        return ParseVersionJson(json);
    }

    /// <summary>Parse version JSON text (the launch pipeline consumes instances/<id>/meta/<id>.json directly).</summary>
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
                // the client mappings (obfuscation table) are only needed by the stage-3 launch flow, so they are ignored here
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

            // 2026 format: natives are encoded directly in the coordinate (…:natives-windows).
            // natives variants for other platforms (…-natives-linux/macos/…) are entirely unusable on this machine → skip.
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

                // legacy format (when classifiers are present): fetch Windows natives separately (extracted during stage-3 launch preparation)
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

    /// <summary>2026 format: Windows natives are expressed by the natives-windows coordinate suffix (x64 mainline).</summary>
    private static bool IsWindowsNativeName(string name)
        => name.EndsWith("natives-windows", StringComparison.Ordinal);

    /// <summary>natives variants for other platforms (including Windows ARM64) → unusable on the x64 mainline.</summary>
    private static bool IsForeignNativeName(string name)
        => name.Contains("natives-linux", StringComparison.Ordinal)
           || name.Contains("natives-macos", StringComparison.Ordinal)
           || name.Contains("natives-freebsd", StringComparison.Ordinal)
           || name.Contains("natives-openbsd", StringComparison.Ordinal)
           || name.Contains("natives-solaris", StringComparison.Ordinal)
           || name.EndsWith("natives-windows-arm64", StringComparison.Ordinal);

    /// <summary>Windows platform decision for Mojang library rules: empty rules → allowed; otherwise converge per allow/deny rule.</summary>
    private static bool EvaluateRules(JsonElement lib)
    {
        if (!lib.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
            return true;

        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var action = rule.TryGetProperty("action", out var a) && a.GetString() == "deny" ? "deny" : "allow";

            // only featureless os rules are supported for now; rules containing features are treated as not applicable (no convergence)
            bool applies = true;
            if (rule.TryGetProperty("os", out var os) && os.ValueKind == JsonValueKind.Object)
            {
                var osName = os.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                applies = osName is "windows" or "";
                if (os.TryGetProperty("arch", out var arch))
                    applies &= arch.GetString() is null or "" or "x64";
            }
            if (rule.TryGetProperty("features", out _))
                applies = false; // TLA/special client features: enabled on demand in stage 3

            if (applies)
                allowed = action == "allow";
        }
        return allowed;
    }

    /// <summary>Fallback derivation of a parsed library relative path: group/artifact/version/name.jar.</summary>
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
