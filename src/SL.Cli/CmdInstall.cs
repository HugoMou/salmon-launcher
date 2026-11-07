// SalmonLauncher CLI — Version install command (simplified, Mojang meta + direct download)
using System.Net.Http.Json;

namespace SL.Cli;

/// <summary>Install a Minecraft version by fetching the official manifest and downloading the client.</summary>
internal static class CmdInstall
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("用法: sl install <version>   (如 sl install 1.21.1)");
            return 1;
        }

        var requested = args[0];
        Console.WriteLine($"正在查询版本清单...");

        var manifest = FetchManifest();
        if (manifest is null)
        {
            Console.Error.WriteLine("无法获取 Mojang 版本清单");
            return 1;
        }

        var target = manifest.versions.FirstOrDefault(v => v.id.Equals(requested, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            var available = manifest.versions.Take(10).Select(v => v.id);
            Console.Error.WriteLine($"未找到版本: {requested}");
            Console.Error.WriteLine($"最新版本示例: {string.Join(", ", available)}");
            return 1;
        }

        var versionDir = Path.Combine(Context.VersionsDir, target.id);
        if (Directory.Exists(versionDir))
        {
            Console.WriteLine($"版本 {target.id} 已经安装在 {versionDir}");
            return 0;
        }

        Console.WriteLine($"下载版本 {target.id} 元数据...");
        var versionInfo = FetchVersionInfo(target.url);
        if (versionInfo is null)
        {
            Console.Error.WriteLine("无法获取版本详情");
            return 1;
        }

        Directory.CreateDirectory(versionDir);
        var infoPath = Path.Combine(versionDir, $"{target.id}.json");
        File.WriteAllText(infoPath, Http.GetStringAsync(target.url).GetAwaiter().GetResult());

        // Download client jar
        if (versionInfo.downloads?.TryGetValue("client", out var client) == true && client.url is not null)
        {
            var jarPath = Path.Combine(versionDir, $"{target.id}.jar");
            Console.WriteLine($"下载 client.jar ({(client.size / 1024 / 1024)} MB)...");
            DownloadFile(client.url, jarPath);
            Console.WriteLine($"✓ 版本 {target.id} 安装完成");
        }

        Console.WriteLine();
        Console.WriteLine($"版本 {target.id} 已安装到: {versionDir}");
        Console.WriteLine($"可以运行: sl instance create mygame --version {target.id}");
        return 0;
    }

    private static Manifest? FetchManifest()
    {
        try
        {
            return Http.GetFromJsonAsync<Manifest>("https://launchermeta.mojang.com/mc/game/version_manifest.json").GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private static VersionInfo? FetchVersionInfo(string url)
    {
        try
        {
            return Http.GetFromJsonAsync<VersionInfo>(url).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private static void DownloadFile(string url, string path)
    {
        var tmp = path + ".part";
        using (var stream = Http.GetStreamAsync(url).GetAwaiter().GetResult())
        using (var file = File.Create(tmp))
        {
            stream.CopyTo(file);
        }
        File.Move(tmp, path, overwrite: true);
    }
}

// ----- DTOs (standalone, no SL.Core dep) -----

internal sealed record Manifest(List<ManifestVersion> versions);
internal sealed record ManifestVersion(string id, string url, string type);
internal sealed record VersionInfo(Dictionary<string, Download>? downloads);
internal sealed record Download(string url, long size);
