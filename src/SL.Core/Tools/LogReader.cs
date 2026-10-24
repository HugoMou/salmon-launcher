using System.IO;

namespace SL.Core.Tools;

/// <summary>阶段六:应用日志 / 实例日志读取(按行尾部读取,含按级别与关键词过滤)。</summary>
/// <param name="Lines">读取的行(自顶向下)</param>
public sealed record LogTail(string File, IReadOnlyList<string> Lines)
{
    public int WarnCount => Lines.Count(l => l.Contains("[WARN]", StringComparison.Ordinal));
    public int ErrorCount => Lines.Count(l => l.Contains("[ERROR]", StringComparison.Ordinal) || l.Contains("[FATAL]", StringComparison.Ordinal));
}

public static class LogReader
{
    /// <summary>最近的启动器自身日志文件(app-YYYYMMDD.log,新→旧)。</summary>
    public static IReadOnlyList<string> LauncherLogs()
    {
        var dir = Boot.Runtime.LogDir;
        if (!Directory.Exists(dir))
            return [];
        return Directory.EnumerateFiles(dir, "app-*.log")
            .OrderByDescending(f => f)
            .ToList();
    }

    /// <summary>尾部读取 lines 行;keyword 空则不过滤。</summary>
    public static LogTail? ReadTail(string file, int lines, string keyword = "")
    {
        if (!File.Exists(file))
            return null;

        string[] all;
        try
        {
            // FileShare.ReadWrite:游戏进程可能正以追加模式写同一文件,不能因读取阻塞它
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var buffer = new List<string>();
            while (reader.ReadLine() is { } line)
                buffer.Add(line);
            all = buffer.ToArray();
        }
        catch (IOException)
        {
            return null;
        }

        var slice = all.Length > lines ? all[^lines..] : all;
        if (!string.IsNullOrWhiteSpace(keyword))
            slice = slice.Where(l => l.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToArray();

        return new LogTail(file, slice);
    }

    /// <summary>实例运行日志(instances/<id>/logs/latest.log)。</summary>
    public static string? LatestInstanceLog(string instanceId)
    {
        var p = Path.Combine(Boot.Runtime.InstancesDir, instanceId, "logs", "latest.log");
        return File.Exists(p) ? p : null;
    }
}
