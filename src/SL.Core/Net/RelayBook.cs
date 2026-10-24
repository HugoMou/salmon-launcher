using System.IO;
using System.Text.Json;

namespace SL.Core.Net;

/// <summary>用户手动添加的中继服务器。</summary>
/// <param name="Name">显示名</param>
/// <param name="Host">地址(IP 或域名)</param>
/// <param name="Port">端口</param>
public sealed record RelayEndpoint(string Name, string Host, int Port)
{
    public string Key => $"{Host}:{Port}";
    public string Display => $"{Name} · {Host}:{Port}";
}

/// <summary>中继服务器收藏夹:保存在数据目录 relays.json,供「添加中继服务器」复用。</summary>
public static class RelayBook
{
    private static string FilePath => Path.Combine(Boot.Runtime.Root, "relays.json");

    public static IReadOnlyList<RelayEndpoint> List()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            var json = File.ReadAllText(FilePath);
            var list = JsonSerializer.Deserialize<List<RelayEndpoint>>(json);
            return list ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public static void Add(RelayEndpoint endpoint)
    {
        var list = List().ToList();
        list.RemoveAll(e => e.Key == endpoint.Key);
        list.Add(endpoint);
        Save(list);
    }

    public static void Remove(string key)
    {
        var list = List().Where(e => e.Key != key).ToList();
        Save(list);
    }

    private static void Save(List<RelayEndpoint> list)
    {
        try
        {
            Directory.CreateDirectory(Boot.Runtime.Root);
            var json = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch (Exception)
        {
            // 收藏夹损坏不影响联机主流程
        }
    }
}