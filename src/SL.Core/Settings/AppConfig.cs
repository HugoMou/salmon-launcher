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

    /// <summary>账户类型:"offline"(离线)或 "msa"(微软正版,会话在 accounts\accounts.json 仓库)。</summary>
    public string AccountType { get; set; } = "offline";

    /// <summary>多账户:活动账户 Id("msa:&lt;uuid&gt;" / "offline:&lt;名字&gt;");空串表示未选择。</summary>
    public string ActiveAccountId { get; set; } = "";

    /// <summary>游戏内存分配(MB),范围 512–32768</summary>
    public int MemoryMb { get; set; } = 4096;

    /// <summary>附加 JVM 参数(空格分隔,插在主类之前)</summary>
    public string JvmExtraArgs { get; set; } = "";

    /// <summary>游戏窗口宽度(像素)</summary>
    public int GameWidth { get; set; } = 854;

    /// <summary>游戏窗口高度(像素)</summary>
    public int GameHeight { get; set; } = 480;

    /// <summary>下载源:"auto"(官方主源+镜像回退) / "bmclapi"(镜像优先)</summary>
    public string DownloadSource { get; set; } = "auto";

    /// <summary>Modrinth 走国内镜像(检索与文件下载)</summary>
    public bool ModrinthMirror { get; set; } = true;

    /// <summary>是否已阅读并同意用户协议</summary>
    public bool TermsAccepted { get; set; }

    /// <summary>界面语言:"auto"(跟随系统)或 zh-Hans / zh-Hant / en / ja</summary>
    public string Language { get; set; } = "auto";

    /// <summary>欢迎页(首次启动初始化)是否已完成;新装或清除数据后为 false</summary>
    public bool OnboardingCompleted { get; set; }
}

public interface ISettingsService
{
    /// <summary>当前配置(就地修改请走 Update,以触发保存与变更事件)</summary>
    AppConfig Current { get; }

    /// <summary>修改配置并原子落盘;任何字段变化都会触发 Changed</summary>
    void Update(Action<AppConfig> mutate);

    event Action<AppConfig>? Changed;
}
