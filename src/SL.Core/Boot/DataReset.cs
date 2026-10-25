using System.IO;
using System.Text.Json;

namespace SL.Core.Boot;

/// <summary>
/// 「清除全部数据」:清空启动器数据根目录,恢复到首次使用的初始状态。
/// 调用方可给出「保留清单」(顶层目录/文件名),清单外的全部删除,
/// 这样未勾选顺带清除的类目会被留下。删除可能因文件被占用(游戏/Java
/// 正在运行等)而部分失败,因此删除前先落一个待清理标记(内容即保留清单),
/// 残留项由下次启动的 RunPending() 继续清除。
/// </summary>
public static class DataReset
{
    private static string Marker => Path.Combine(Data.AppPaths.Root, ".reset-pending");

    /// <summary>启动时调用:若上次清除留有标记,按标记的保留清单把残留删干净。</summary>
    public static void RunPending()
    {
        if (!File.Exists(Marker))
            return;

        List<string>? keep = null;
        try
        {
            var text = File.ReadAllText(Marker).Trim();
            if (text.Length > 0 && text != "*")
                keep = JsonSerializer.Deserialize<List<string>>(text);
        }
        catch (Exception)
        {
            // 标记损坏:按用户当初的整体意图(全部清除)处理
            keep = null;
        }
        Purge(keep);
    }

    /// <summary>
    /// 清除数据根目录:保留 keep 列出的顶层项与标记文件本身,其余全删。
    /// keep 为 null/空即「一个不留」。返回未能删除的路径(被占用),空列表表示已彻底清空。
    /// </summary>
    public static List<string> Purge(IReadOnlyCollection<string>? keep = null)
    {
        var failed = new List<string>();
        var root = Data.AppPaths.Root;
        var keepSet = new HashSet<string>(keep ?? [], StringComparer.OrdinalIgnoreCase);
        var markerName = Path.GetFileName(Marker);

        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Marker, keepSet.Count == 0 ? "*" : JsonSerializer.Serialize(keepSet));
        }
        catch (Exception)
        {
            return [root];
        }

        foreach (var path in Directory.EnumerateFileSystemEntries(root).ToList())
        {
            var name = Path.GetFileName(path);
            if (string.Equals(name, markerName, StringComparison.OrdinalIgnoreCase) || keepSet.Contains(name))
                continue;
            try
            {
                // 只读文件(部分库/校验文件)先解除只读再删
                var attr = File.GetAttributes(path);
                if ((attr & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(path, attr & ~FileAttributes.ReadOnly);

                if (Directory.Exists(path))
                    Directory.Delete(path, true);
                else
                    File.Delete(path);
            }
            catch (Exception)
            {
                failed.Add(path);
            }
        }

        if (failed.Count == 0)
        {
            try { File.Delete(Marker); } catch (Exception) { /* 标记删除失败会在下次启动再清一次 */ }
        }
        return failed;
    }
}
