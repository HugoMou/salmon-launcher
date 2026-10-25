using System.IO;

namespace SL.Core.Boot;

/// <summary>
/// 「清除全部数据」:清空启动器数据根目录,恢复到首次使用的初始状态。
/// 删除可能因文件被占用(游戏/Java 正在运行、壁纸位图等)而部分失败,
/// 因此删除前先落一个待清理标记,残留项由下次启动的 RunPending() 继续清除。
/// </summary>
public static class DataReset
{
    private static string Marker => Path.Combine(Data.AppPaths.Root, ".reset-pending");

    /// <summary>启动时调用:若上次清除留有标记,继续把没删掉的残留删干净。</summary>
    public static void RunPending()
    {
        if (!File.Exists(Marker))
            return;
        Purge();
    }

    /// <summary>
    /// 清除数据根目录下的全部内容(保留标记文件本身)。
    /// 返回未能删除的路径(被占用),空列表表示已彻底清空。
    /// </summary>
    public static List<string> Purge()
    {
        var failed = new List<string>();
        var root = Data.AppPaths.Root;
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Marker, DateTime.Now.ToString("O"));
        }
        catch (Exception)
        {
            return [root];
        }

        foreach (var path in Directory.EnumerateFileSystemEntries(root).ToList())
        {
            if (string.Equals(path, Marker, StringComparison.OrdinalIgnoreCase))
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
