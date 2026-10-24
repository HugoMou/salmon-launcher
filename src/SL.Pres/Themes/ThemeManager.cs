using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace SL.Pres.Themes;

/// <summary>一套完整配色（亮/暗两套色板）。</summary>
public sealed record ThemePalette(
    string Id,
    string Name,
    string Hint,
    IReadOnlyDictionary<string, string> Dark,
    IReadOnlyDictionary<string, string> Light);

/// <summary>强调色（与配色方案正交，可任意组合）。</summary>
public sealed record AccentChoice(string Id, string Name, string Dark, string Light);

/// <summary>
/// 主题切换：令牌字典负责基础色（亮/暗），配色方案覆盖表层色，强调色再覆盖 Accent 系列。
/// 全部写入 Application.Resources 的直接键（查找优先级高于合并字典），
/// 因此使用 DynamicResource 的控件会立即生效，不需要重建页面。
/// </summary>
public static class ThemeManager
{
    private static bool _hooked;
    private static readonly List<string> OverriddenKeys = [];

    /// <summary>用户偏好（含跟随系统语义）</summary>
    public static Core.Settings.ThemePref Current { get; private set; } = Core.Settings.ThemePref.FollowSystem;

    /// <summary>当前配色方案 Id</summary>
    public static string PaletteId { get; private set; } = "salmon";

    /// <summary>当前强调色 Id</summary>
    public static string AccentId { get; private set; } = "coral";

    /// <summary>主题实际生效后触发（窗口背景、Mica 深浅色需要跟随刷新）。</summary>
    public static event Action? ThemeChanged;

    public static Core.Settings.ThemePref Resolved =>
        Current == Core.Settings.ThemePref.FollowSystem
            ? (IsOsDark() ? Core.Settings.ThemePref.Dark : Core.Settings.ThemePref.Light)
            : Current;

    public static bool IsDark => Resolved == Core.Settings.ThemePref.Dark;

    public static IReadOnlyList<ThemePalette> Palettes { get; } =
    [
        new("salmon", "鲑鱼暖橙", "中性灰底 + 珊瑚强调，默认配色",
            new Dictionary<string, string>(),
            new Dictionary<string, string>()),
        new("ink", "墨蓝", "冷色蓝灰底，适合夜间长时间使用",
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
        new("sand", "暖沙", "暖米色纸感底，柔和护眼",
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
        new("slate", "石板灰", "低饱和中性灰，界面更沉稳",
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

    public static IReadOnlyList<AccentChoice> Accents { get; } =
    [
        new("coral", "珊瑚橙", "#FA8072", "#E2604B"),
        new("ocean", "海洋蓝", "#4CC2FF", "#0078D4"),
        new("forest", "森林绿", "#55C97F", "#107C41"),
        new("violet", "薰衣紫", "#B68CFF", "#7B3FE4"),
        new("amber", "琥珀金", "#E8B14C", "#B7791F"),
    ];

    public static bool IsOsDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int lightTheme)
                return lightTheme == 0;
        }
        catch
        {
            // 读取失败：默认深色
        }
        return true;
    }

    public static void Apply(Core.Settings.ThemePref pref, string paletteId, string accentId)
    {
        Current = pref;
        PaletteId = Palettes.Any(p => p.Id == paletteId) ? paletteId : "salmon";
        AccentId = Accents.Any(a => a.Id == accentId) ? accentId : "coral";
        HookSystemEvents();
        ApplyTokens();
    }

    public static void ApplyFromPref(Core.Settings.ThemePref pref)
        => Apply(pref, PaletteId, AccentId);

    private static void HookSystemEvents()
    {
        if (_hooked)
            return;
        _hooked = true;
        try
        {
            SystemEvents.UserPreferenceChanged += (_, args) =>
            {
                if (args.Category == UserPreferenceCategory.General && Current == Core.Settings.ThemePref.FollowSystem)
                    ApplyTokens();
            };
        }
        catch
        {
            // SystemEvents 不可用时忽略跟随系统刷新
        }
    }

    private static void ApplyTokens()
    {
        var app = Application.Current;
        if (app is null || app.Resources.MergedDictionaries.Count == 0)
            return;

        var dark = IsDark;
        var uri = dark ? "Themes/Tokens.Dark.xaml" : "Themes/Tokens.Light.xaml";
        app.Resources.MergedDictionaries[0] = new ResourceDictionary { Source = new Uri(uri, UriKind.Relative) };

        // 清除上一次的覆盖键，避免切换配色后残留旧色
        foreach (var key in OverriddenKeys)
            app.Resources.Remove(key);
        OverriddenKeys.Clear();

        var palette = Palettes.FirstOrDefault(p => p.Id == PaletteId) ?? Palettes[0];
        foreach (var pair in dark ? palette.Dark : palette.Light)
            Override(app, pair.Key, pair.Value);

        var accent = Accents.FirstOrDefault(a => a.Id == AccentId) ?? Accents[0];
        var baseColor = Parse(dark ? accent.Dark : accent.Light);
        Override(app, "Brush.Accent", baseColor);
        Override(app, "Brush.AccentHover", dark ? Lighten(baseColor, 0.14) : Darken(baseColor, 0.10));
        Override(app, "Brush.AccentPress", dark ? Darken(baseColor, 0.10) : Darken(baseColor, 0.20));
        Override(app, "Brush.AccentSoft", WithAlpha(baseColor, 0x2E));
        Override(app, "Brush.AccentFg", dark ? Color.FromRgb(0x0B, 0x0F, 0x12) : Colors.White);
        // 导航选中底色跟随强调色（配色方案未显式覆盖时）
        if (!(dark ? palette.Dark : palette.Light).ContainsKey("Brush.NavItemChecked"))
            Override(app, "Brush.NavItemChecked", WithAlpha(baseColor, 0x1F));

        ThemeChanged?.Invoke();
    }

    private static void Override(Application app, string key, string hex)
        => Override(app, key, Parse(hex));

    private static void Override(Application app, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        app.Resources[key] = brush;
        OverriddenKeys.Add(key);
    }

    private static Color Parse(string hex)
        => (Color)ColorConverter.ConvertFromString(hex)!;

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    private static Color Lighten(Color c, double amount)
        => Color.FromRgb(
            (byte)Math.Clamp(c.R + (255 - c.R) * amount, 0, 255),
            (byte)Math.Clamp(c.G + (255 - c.G) * amount, 0, 255),
            (byte)Math.Clamp(c.B + (255 - c.B) * amount, 0, 255));

    private static Color Darken(Color c, double amount)
        => Color.FromRgb(
            (byte)Math.Clamp(c.R * (1 - amount), 0, 255),
            (byte)Math.Clamp(c.G * (1 - amount), 0, 255),
            (byte)Math.Clamp(c.B * (1 - amount), 0, 255));
}
