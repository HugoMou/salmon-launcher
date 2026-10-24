namespace SL.Data.Settings;

using SL.Data.Storage;

/// <summary>全局设置的数据载体(与业务层的配置模型保持独立,方便字段演进)。</summary>
public class AppSettings
{
    /// <summary>"dark" | "light" | "system"</summary>
    public string ThemeMode { get; set; } = "system";

    /// <summary>配色方案：salmon / ink / sand / slate</summary>
    public string ThemePalette { get; set; } = "salmon";

    /// <summary>强调色：coral / ocean / forest / violet / amber</summary>
    public string AccentColor { get; set; } = "coral";

    /// <summary>下载并发数上限</summary>
    public int TransferConcurrency { get; set; } = 6;

    /// <summary>阶段五:离线账户名(游戏内玩家名)</summary>
    public string AccountName { get; set; } = "Rio";

    /// <summary>账户类型:"offline"(离线)/ "msa"(微软正版)。</summary>
    public string AccountType { get; set; } = "offline";
}

/// <summary>设置文件的读写入口(原子落盘)。</summary>
public sealed class AppSettingsStore
{
    private readonly string _path;

    public AppSettingsStore(string path) => _path = path;

    public AppSettings Load() => AtomicJsonFile.Load<AppSettings>(_path);

    public void Save(AppSettings settings) => AtomicJsonFile.Save(_path, settings);
}
