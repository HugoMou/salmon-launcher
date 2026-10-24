using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SL.App.Themes;

/// <summary>一套完整配色(亮/暗两套色板),可附带一张壁纸。</summary>
public sealed record ThemePalette(
    string Id,
    string Name,
    string Hint,
    IReadOnlyDictionary<string, string> Dark,
    IReadOnlyDictionary<string, string> Light,
    string? Background = null);

/// <summary>强调色(与配色方案正交,可任意组合)。</summary>
public sealed record AccentChoice(string Id, string Name, string Dark, string Light);

/// <summary>
/// WinUI 3 主题切换:
/// - 全部自定义 Brush 令牌写入 Application.Resources.ThemeDictionaries("Light"/"Default"),
///   页面以 ThemeResource 引用,RequestedTheme 切换时框架自动重估,页面无需重建;
/// - 同时覆盖 SystemAccentColor 系列,让原生控件(按钮/滑块/开关/导航选中)跟随自定义强调色;
/// - 深浅色跟随系统:注册表读取 + SystemEvents 监听。
/// </summary>
public static class ThemeManager
{
    private static bool _hooked;

    /// <summary>用户偏好(含跟随系统语义)</summary>
    public static Core.Settings.ThemePref Current { get; private set; } = Core.Settings.ThemePref.FollowSystem;

    /// <summary>当前配色方案 Id</summary>
    public static string PaletteId { get; private set; } = "salmon";

    /// <summary>当前强调色 Id</summary>
    public static string AccentId { get; private set; } = "coral";

    /// <summary>当前主题附带的壁纸(绝对路径);null 表示无壁纸。</summary>
    public static string? BackgroundImage { get; private set; }

    /// <summary>主题实际生效后触发(根元素 RequestedTheme 需要跟随刷新)。</summary>
    public static event Action? ThemeChanged;

    public static Core.Settings.ThemePref Resolved =>
        Current == Core.Settings.ThemePref.FollowSystem
            ? (IsOsDark() ? Core.Settings.ThemePref.Dark : Core.Settings.ThemePref.Light)
            : Current;

    public static bool IsDark => Resolved == Core.Settings.ThemePref.Dark;

    // ---------------------------------------------------------- salmon 基础色板(旧 Tokens.Dark/Light.xaml)

    private static readonly Dictionary<string, string> BaseDark = new()
    {
        ["Brush.PageBg"] = "#13191D", ["Brush.PanelBg"] = "#101519",
        ["Brush.Card"] = "#1C252B", ["Brush.CardHover"] = "#243038", ["Brush.CardPress"] = "#1A2328",
        ["Brush.Border"] = "#29323A", ["Brush.BorderStrong"] = "#3A454F",
        ["Brush.Fg"] = "#E9EEF0", ["Brush.FgDim"] = "#9FB0B7", ["Brush.FgFaint"] = "#6E7C84",
        ["Brush.WindowTint"] = "#D90F1418", ["Brush.NavBg"] = "#B80F1418",
        ["Brush.NavItemHover"] = "#14FFFFFF", ["Brush.NavItemChecked"] = "#1FFA8072",
    };

    private static readonly Dictionary<string, string> BaseLight = new()
    {
        ["Brush.PageBg"] = "#EDF1F2", ["Brush.PanelBg"] = "#E2E8EA",
        ["Brush.Card"] = "#FFFFFF", ["Brush.CardHover"] = "#F4F8F9", ["Brush.CardPress"] = "#E9EDEE",
        ["Brush.Border"] = "#D0D8DB", ["Brush.BorderStrong"] = "#AFC0C6",
        ["Brush.Fg"] = "#1B2A31", ["Brush.FgDim"] = "#5A6E78", ["Brush.FgFaint"] = "#90A2A9",
        ["Brush.WindowTint"] = "#D9F3F6F8", ["Brush.NavBg"] = "#B8E9EEF1",
        ["Brush.NavItemHover"] = "#0F000000", ["Brush.NavItemChecked"] = "#1FE2604B",
    };

    // ---------------------------------------------------------- 固定语义色(成功/警告/危险)

    private static readonly Dictionary<string, string> StatusDark = new()
    {
        ["Brush.Success"] = "#54C97F", ["Brush.SuccessSoft"] = "#2654C97F",
        ["Brush.Warning"] = "#E3B256", ["Brush.WarningSoft"] = "#2FE3B256",
        ["Brush.Danger"] = "#EA6A55", ["Brush.DangerSoft"] = "#2BEA6A55",
        ["Brush.Overlay"] = "#99000000",
    };

    private static readonly Dictionary<string, string> StatusLight = new()
    {
        ["Brush.Success"] = "#2F9F63", ["Brush.SuccessSoft"] = "#262F9F63",
        ["Brush.Warning"] = "#BE7A19", ["Brush.WarningSoft"] = "#26BE7A19",
        ["Brush.Danger"] = "#C2463A", ["Brush.DangerSoft"] = "#26C2463A",
        ["Brush.Overlay"] = "#66000000",
    };

    // ---------------------------------------------------------- 配色方案(覆盖 salmon 基础色板)

    private static readonly ThemePalette[] BuiltIn =
    [
        new("salmon", "鲑鱼暖橙", "中性灰底 + 珊瑚强调,默认配色",
            new Dictionary<string, string>(),
            new Dictionary<string, string>()),
        new("ink", "墨蓝", "冷色蓝灰底,适合夜间长时间使用",
            new Dictionary<string, string>
            {
                ["Brush.PageBg"] = "#0E151C", ["Brush.PanelBg"] = "#0B1117",
                ["Brush.Card"] = "#16212B", ["Brush.CardHover"] = "#1D2A36", ["Brush.CardPress"] = "#131D26",
                ["Brush.Border"] = "#253442", ["Brush.BorderStrong"] = "#35485A",
                ["Brush.Fg"] = "#E6EDF3", ["Brush.FgDim"] = "#9BAEBF", ["Brush.FgFaint"] = "#6B8090",
                ["Brush.WindowTint"] = "#D90B1117", ["Brush.NavBg"] = "#B80B1117",
                ["Brush.NavItemHover"] = "#14FFFFFF", ["Brush.NavItemChecked"] = "#1F4CC2FF",
            },
            new Dictionary<string, string>
            {
                ["Brush.PageBg"] = "#EAF0F5", ["Brush.PanelBg"] = "#DFE8EF",
                ["Brush.Card"] = "#FFFFFF", ["Brush.CardHover"] = "#F2F7FB", ["Brush.CardPress"] = "#E6EDF3",
                ["Brush.Border"] = "#CFDBE5", ["Brush.BorderStrong"] = "#AFC1D1",
                ["Brush.Fg"] = "#16232E", ["Brush.FgDim"] = "#55707F", ["Brush.FgFaint"] = "#8AA0AC",
                ["Brush.WindowTint"] = "#D9EDF3F8", ["Brush.NavBg"] = "#B8E4EDF4",
                ["Brush.NavItemHover"] = "#0F000000", ["Brush.NavItemChecked"] = "#1F0078D4",
            }),
        new("sand", "暖沙", "暖米色纸感底,柔和护眼",
            new Dictionary<string, string>
            {
                ["Brush.PageBg"] = "#191411", ["Brush.PanelBg"] = "#14100D",
                ["Brush.Card"] = "#241D18", ["Brush.CardHover"] = "#2E251E", ["Brush.CardPress"] = "#1F1915",
                ["Brush.Border"] = "#3A2F26", ["Brush.BorderStrong"] = "#4E4033",
                ["Brush.Fg"] = "#F0E8DF", ["Brush.FgDim"] = "#BCAA98", ["Brush.FgFaint"] = "#8C7A68",
                ["Brush.WindowTint"] = "#D914100D", ["Brush.NavBg"] = "#B814100D",
                ["Brush.NavItemHover"] = "#14FFFFFF", ["Brush.NavItemChecked"] = "#1FFA8072",
            },
            new Dictionary<string, string>
            {
                ["Brush.PageBg"] = "#F6F0E7", ["Brush.PanelBg"] = "#EDE4D8",
                ["Brush.Card"] = "#FFFDFA", ["Brush.CardHover"] = "#FBF5EC", ["Brush.CardPress"] = "#F2EBE0",
                ["Brush.Border"] = "#DFD2C0", ["Brush.BorderStrong"] = "#C7B69F",
                ["Brush.Fg"] = "#2B2117", ["Brush.FgDim"] = "#6D5B49", ["Brush.FgFaint"] = "#9C8A76",
                ["Brush.WindowTint"] = "#D9F6F0E7", ["Brush.NavBg"] = "#B8EFE6DA",
                ["Brush.NavItemHover"] = "#0F000000", ["Brush.NavItemChecked"] = "#1FBE7A19",
            }),
        new("slate", "石板灰", "低饱和中性灰,界面更沉稳",
            new Dictionary<string, string>
            {
                ["Brush.PageBg"] = "#141517", ["Brush.PanelBg"] = "#101113",
                ["Brush.Card"] = "#1D1F23", ["Brush.CardHover"] = "#26292E", ["Brush.CardPress"] = "#191B1F",
                ["Brush.Border"] = "#2B2E34", ["Brush.BorderStrong"] = "#3C4148",
                ["Brush.Fg"] = "#ECEDEF", ["Brush.FgDim"] = "#A3A7AE", ["Brush.FgFaint"] = "#71767D",
                ["Brush.WindowTint"] = "#D9101113", ["Brush.NavBg"] = "#B8101113",
                ["Brush.NavItemHover"] = "#14FFFFFF", ["Brush.NavItemChecked"] = "#1FB68CFF",
            },
            new Dictionary<string, string>
            {
                ["Brush.PageBg"] = "#F1F2F4", ["Brush.PanelBg"] = "#E7E9EC",
                ["Brush.Card"] = "#FFFFFF", ["Brush.CardHover"] = "#F6F7F9", ["Brush.CardPress"] = "#ECEEF1",
                ["Brush.Border"] = "#D6D9DE", ["Brush.BorderStrong"] = "#B5BAC2",
                ["Brush.Fg"] = "#1E2126", ["Brush.FgDim"] = "#5C626B", ["Brush.FgFaint"] = "#9298A1",
                ["Brush.WindowTint"] = "#D9F1F2F4", ["Brush.NavBg"] = "#B8EAECEF",
                ["Brush.NavItemHover"] = "#0F000000", ["Brush.NavItemChecked"] = "#1F0078D4",
            }),
    ];

    /// <summary>主题工坊生成的配色(按需注册,持久化在 themes.json)。</summary>
    private static readonly List<ThemePalette> Custom = [];

    /// <summary>全部配色(内置 + 工坊生成)。</summary>
    public static IReadOnlyList<ThemePalette> Palettes => [.. BuiltIn, .. Custom];

    /// <summary>注册(或覆盖)一套自定义配色。</summary>
    public static void RegisterPalette(ThemePalette palette)
    {
        Custom.RemoveAll(p => p.Id == palette.Id);
        Custom.Add(palette);
    }

    /// <summary>配色预览用的三个代表色(底色 / 卡片 / 边框)。</summary>
    public static IReadOnlyList<string> PreviewColors(ThemePalette palette)
    {
        string Pick(string key)
        {
            if (palette.Dark.TryGetValue(key, out var v))
                return v;
            return BaseDark.TryGetValue(key, out var b) ? b : "#7A7A7A";
        }
        return [Pick("Brush.PageBg"), Pick("Brush.Card"), Pick("Brush.BorderStrong")];
    }

    private static string CustomPath(string root) => Path.Combine(root, "themes.json");

    /// <summary>工坊主题的落盘结构。</summary>
    private sealed record ThemeCache(List<ThemePalette> Palettes, List<AccentChoice> Accents);

    /// <summary>从磁盘恢复工坊生成的主题(启动时调用,失败静默)。</summary>
    public static void LoadCustom(string root)
    {
        try
        {
            var path = CustomPath(root);
            if (!File.Exists(path))
                return;
            var cache = System.Text.Json.JsonSerializer.Deserialize<ThemeCache>(File.ReadAllText(path));
            if (cache is null)
                return;
            foreach (var p in cache.Palettes ?? [])
            {
                if (string.IsNullOrWhiteSpace(p.Id) || p.Dark.Count == 0)
                    continue;
                RegisterPalette(p);
            }
            foreach (var a in cache.Accents ?? [])
            {
                if (!string.IsNullOrWhiteSpace(a.Id))
                    RegisterAccent(a);
            }
        }
        catch
        {
            // 主题缓存损坏不影响启动
        }
    }

    /// <summary>把工坊生成的主题写回磁盘。</summary>
    public static void SaveCustom(string root)
    {
        try
        {
            var path = CustomPath(root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var cache = new ThemeCache([.. Custom], [.. CustomAccents]);
            File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(cache));
        }
        catch
        {
            // 写盘失败不打断使用
        }
    }

    private static readonly AccentChoice[] BuiltInAccents =
    [
        new("coral", "珊瑚橙", "#FA8072", "#E2604B"),
        new("ocean", "海洋蓝", "#4CC2FF", "#0078D4"),
        new("forest", "森林绿", "#55C97F", "#107C41"),
        new("violet", "薰衣紫", "#B68CFF", "#7B3FE4"),
        new("amber", "琥珀金", "#E8B14C", "#B7791F"),
    ];

    private static readonly List<AccentChoice> CustomAccents = [];

    public static IReadOnlyList<AccentChoice> Accents => [.. BuiltInAccents, .. CustomAccents];

    /// <summary>注册(或覆盖)一个由主题工坊生成的强调色。</summary>
    public static void RegisterAccent(AccentChoice accent)
    {
        CustomAccents.RemoveAll(a => a.Id == accent.Id);
        CustomAccents.Add(accent);
    }

    // ---------------------------------------------------------- 应用

    public static void Apply(Core.Settings.ThemePref pref, string paletteId, string accentId)
    {
        Current = pref;
        PaletteId = Palettes.Any(p => p.Id == paletteId) ? paletteId : "salmon";
        AccentId = Accents.Any(a => a.Id == accentId) ? accentId : "coral";
        var palette = Palettes.FirstOrDefault(p => p.Id == PaletteId);
        BackgroundImage = palette?.Background is { Length: > 0 } path && File.Exists(path) ? path : null;
        HookSystemEvents();
        ApplyTokens();
    }

    public static void ApplyFromPref(Core.Settings.ThemePref pref)
        => Apply(pref, PaletteId, AccentId);

    public static bool IsOsDark()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int lightTheme)
                return lightTheme == 0;
        }
        catch
        {
            // 读取失败:默认深色
        }
        return true;
    }

    private static void HookSystemEvents()
    {
        if (_hooked)
            return;
        _hooked = true;
        try
        {
            Microsoft.Win32.SystemEvents.UserPreferenceChanged += (_, args) =>
            {
                if (args.Category == Microsoft.Win32.UserPreferenceCategory.General
                    && Current == Core.Settings.ThemePref.FollowSystem)
                    ApplyTokens();
            };
        }
        catch
        {
            // SystemEvents 不可用时忽略跟随系统刷新
        }
    }

    /// <summary>把当前配色写入两套主题字典。每次整体重写,不残留旧色。</summary>
    private static void ApplyTokens()
    {
        var app = Application.Current;
        if (app is null)
            return;

        var (light, dark) = EnsureThemeDictionaries(app);

        var palette = Palettes.FirstOrDefault(p => p.Id == PaletteId) ?? Palettes[0];
        var darkSrc = Merge(BaseDark, StatusDark, palette.Dark);
        var lightSrc = Merge(BaseLight, StatusLight, palette.Light);

        var accent = Accents.FirstOrDefault(a => a.Id == AccentId) ?? Accents[0];
        var accentDark = Parse(accent.Dark);
        var accentLight = Parse(accent.Light);

        Fill(dark, darkSrc);
        Fill(light, lightSrc);

        // 壁纸存在时页面底色半透明,让壁纸透出来
        var pageDark = Parse(darkSrc.TryGetValue("Brush.PageBg", out var pd) ? pd : "#13191D");
        var pageLight = Parse(lightSrc.TryGetValue("Brush.PageBg", out var pl) ? pl : "#EDF1F2");
        Put(dark, "Brush.PageBgTrans", WithAlpha(pageDark, 0xDA));
        Put(light, "Brush.PageBgTrans", WithAlpha(pageLight, 0xDE));
        Put(dark, "Brush.WallpaperScrim", Argb(0x55, 0, 0, 0));
        Put(light, "Brush.WallpaperScrim", Argb(0x44, 0xFF, 0xFF, 0xFF));

        // 强调色系列(写入两套字典,值随主题变体)
        Put(dark, "Brush.Accent", accentDark);
        Put(dark, "Brush.AccentHover", Lighten(accentDark, 0.14));
        Put(dark, "Brush.AccentPress", Darken(accentDark, 0.10));
        Put(dark, "Brush.AccentSoft", WithAlpha(accentDark, 0x2E));
        Put(dark, "Brush.AccentFg", Rgb(0x0B, 0x0F, 0x12));
        Put(light, "Brush.Accent", accentLight);
        Put(light, "Brush.AccentHover", Darken(accentLight, 0.10));
        Put(light, "Brush.AccentPress", Darken(accentLight, 0.20));
        Put(light, "Brush.AccentSoft", WithAlpha(accentLight, 0x30));
        Put(light, "Brush.AccentFg", Rgb(0xFF, 0xFF, 0xFF));

        // 导航选中底色跟随强调色(配色方案未显式覆盖时)
        if (!darkSrc.ContainsKey("Brush.NavItemChecked"))
            Put(dark, "Brush.NavItemChecked", WithAlpha(accentDark, 0x1F));
        if (!lightSrc.ContainsKey("Brush.NavItemChecked"))
            Put(light, "Brush.NavItemChecked", WithAlpha(accentLight, 0x1F));

        // 覆盖 SystemAccentColor 系列 → 原生控件跟随自定义强调色
        WriteSystemAccent(dark, accentDark);
        WriteSystemAccent(light, accentLight);

        ThemeChanged?.Invoke();
    }

    private static void WriteSystemAccent(ResourceDictionary dict, Color accent)
    {
        dict["SystemAccentColor"] = accent;
        dict["SystemAccentColorLight1"] = Lighten(accent, 0.14);
        dict["SystemAccentColorLight2"] = Lighten(accent, 0.28);
        dict["SystemAccentColorLight3"] = Lighten(accent, 0.42);
        dict["SystemAccentColorDark1"] = Darken(accent, 0.10);
        dict["SystemAccentColorDark2"] = Darken(accent, 0.20);
        dict["SystemAccentColorDark3"] = Darken(accent, 0.30);
    }

    private static (ResourceDictionary Light, ResourceDictionary Dark) EnsureThemeDictionaries(Application app)
    {
        var themes = app.Resources.ThemeDictionaries;

        ResourceDictionary light;
        if (themes.ContainsKey("Light") && themes["Light"] is ResourceDictionary l)
        {
            light = l;
        }
        else
        {
            light = new ResourceDictionary();
            themes["Light"] = light;
        }

        ResourceDictionary dark;
        if (themes.ContainsKey("Default") && themes["Default"] is ResourceDictionary d)
        {
            dark = d;
        }
        else
        {
            dark = new ResourceDictionary();
            themes["Default"] = dark;
        }

        return (light, dark);
    }

    private static Dictionary<string, string> Merge(
        Dictionary<string, string> baseMap,
        Dictionary<string, string> status,
        IReadOnlyDictionary<string, string> palette)
    {
        var merged = new Dictionary<string, string>(baseMap);
        foreach (var kv in status)
            merged[kv.Key] = kv.Value;
        foreach (var kv in palette)
            merged[kv.Key] = kv.Value;
        return merged;
    }

    private static void Fill(ResourceDictionary dict, Dictionary<string, string> src)
    {
        foreach (var kv in src)
            dict[kv.Key] = MakeBrush(kv.Value);
    }

    private static void Put(ResourceDictionary dict, string key, Color color)
        => dict[key] = MakeBrush(color);

    private static SolidColorBrush MakeBrush(string hex) => new(Parse(hex));
    private static SolidColorBrush MakeBrush(Color color) => new(color);

    private static Color Parse(string hex)
    {
        var s = hex.TrimStart('#');
        byte R(byte i) => Convert.ToByte(s.Substring(i, 2), 16);
        return s.Length >= 8
            ? Argb(R(0), R(2), R(4), R(6))
            : Rgb(R(0), R(2), R(4));
    }

    /// <summary>把 "#RRGGBB"/"#AARRGGBB" 转成颜色(供页面预览色块使用)。</summary>
    public static Color FromHex(string hex) => Parse(hex);

    private static Color Rgb(byte r, byte g, byte b) => new() { A = 255, R = r, G = g, B = b };

    private static Color Argb(byte a, byte r, byte g, byte b) => new() { A = a, R = r, G = g, B = b };

    private static Color WithAlpha(Color c, byte alpha) => Argb(alpha, c.R, c.G, c.B);

    private static Color Lighten(Color c, double amount)
        => Rgb(
            (byte)Math.Clamp(c.R + (255 - c.R) * amount, 0, 255),
            (byte)Math.Clamp(c.G + (255 - c.G) * amount, 0, 255),
            (byte)Math.Clamp(c.B + (255 - c.B) * amount, 0, 255));

    private static Color Darken(Color c, double amount)
        => Rgb(
            (byte)Math.Clamp(c.R * (1 - amount), 0, 255),
            (byte)Math.Clamp(c.G * (1 - amount), 0, 255),
            (byte)Math.Clamp(c.B * (1 - amount), 0, 255));
}
