namespace SL.Core.Settings;

/// <summary>主题偏好。FollowSystem 表示跟随 Windows 深浅色系统设置。</summary>
public enum ThemePref
{
    Dark = 0,
    Light = 1,
    FollowSystem = 2,
}

/// <summary>业务层配置模型(表示层可见的唯一配置形态)。</summary>
public sealed class AppConfig
{
    public ThemePref Theme { get; set; } = ThemePref.FollowSystem;

    /// <summary>配色方案 Id：salmon / ink / sand / slate</summary>
    public string Palette { get; set; } = "salmon";

    /// <summary>强调色 Id：coral / ocean / forest / violet / amber</summary>
    public string Accent { get; set; } = "coral";

    public int TransferConcurrency { get; set; } = 6;

    /// <summary>阶段五:当前离线账户名(游戏内玩家名)。</summary>
    public string AccountName { get; set; } = "Rio";

    /// <summary>账户类型:"offline"(离线)或 "msa"(微软正版,取 accounts\msa.json 会话)。</summary>
    public string AccountType { get; set; } = "offline";
}

public interface ISettingsService
{
    /// <summary>当前配置(就地修改请走 Update,以触发保存与变更事件)</summary>
    AppConfig Current { get; }

    /// <summary>修改配置并原子落盘;任何字段变化都会触发 Changed</summary>
    void Update(Action<AppConfig> mutate);

    event Action<AppConfig>? Changed;
}
