using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SL.Data.Storage;

/// <summary>
/// 原子化 JSON 文件读写:写入时先落临时文件,成功后整体替换目标文件。
/// 这样启动器在任何时刻崩溃,目录里要么是旧配置、要么是完整新配置,
/// 不会出现写了一半的损坏文件。
/// </summary>
public static class AtomicJsonFile
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>读取配置;文件缺失或损坏时返回默认实例(throwNotFound=false 行为由调用方决定是否提示)。</summary>
    public static T Load<T>(string path) where T : new()
    {
        try
        {
            if (!File.Exists(path))
                return new T();

            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var value = JsonSerializer.Deserialize<T>(stream, Options);
            return value ?? new T();
        }
        catch (Exception)
        {
            // 配置损坏:回退默认值。调用方如需告知用户,可先探测文件存在性。
            return new T();
        }
    }

    public static void Save<T>(string path, T value)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        using (var stream = File.Create(tmp))
        {
            JsonSerializer.Serialize(stream, value, Options);
        }

        // File.Move 原子替换(Windows 上 overwrite:true 走 ReplaceFile 语义)
        File.Move(tmp, path, overwrite: true);
    }
}
