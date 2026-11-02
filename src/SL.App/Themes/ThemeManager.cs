// SalmonLauncher (SL) — a Minecraft Java edition launcher
// Copyright (C) 2026 Hugo Mou
//
// This program is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free Software
// Foundation, either version 3 of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS FOR A
// PARTICULAR PURPOSE. See the GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License along with
// this program. If not, see <https://www.gnu.org/licenses/>.
//
// SPDX-License-Identifier: GPL-3.0-or-later
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SL.App.Themes;

/// <summary>A complete color scheme (light/dark palettes), optionally with a wallpaper.</summary>
public sealed record ThemePalette(
    string Id,
    string Name,
    string Hint,
    IReadOnlyDictionary<string, string> Dark,
    IReadOnlyDictionary<string, string> Light,
    string? Background = null);

/// <summary>Accent color (orthogonal to the palette and freely combinable).</summary>
public sealed record AccentChoice(string Id, string Name, string Dark, string Light);

/// <summary>
/// WinUI 3 theme switching:
/// - All custom Brush tokens are written into Application.Resources.ThemeDictionaries ("Light"/"Default"),
///   referenced by pages via ThemeResource, so the framework re-evaluates them automatically when RequestedTheme changes and pages need no rebuild;
/// - The SystemAccentColor family is also overridden so native controls (buttons/sliders/toggles/nav selection) follow the custom accent;
/// - Light/dark follows the system: registry read + SystemEvents listener.
/// </summary>
public static class ThemeManager
{
    private static bool _hooked;

    /// <summary>User preference (including the follow-system semantics)</summary>
    public static Core.Settings.ThemePref Current { get; private set; } = Core.Settings.ThemePref.FollowSystem;

    /// <summary>Current palette Id</summary>
    public static string PaletteId { get; private set; } = "salmon";

    /// <summary>Current accent Id</summary>
    public static string AccentId { get; private set; } = "coral";

    /// <summary>Wallpaper attached to the current theme (absolute path); null means no wallpaper.</summary>
    public static string? BackgroundImage { get; private set; }

    /// <summary>Raised after the theme actually takes effect (the root element's RequestedTheme must be refreshed accordingly).</summary>
    public static event Action? ThemeChanged;

    public static Core.Settings.ThemePref Resolved =>
        Current == Core.Settings.ThemePref.FollowSystem
            ? (IsOsDark() ? Core.Settings.ThemePref.Dark : Core.Settings.ThemePref.Light)
            : Current;

    public static bool IsDark => Resolved == Core.Settings.ThemePref.Dark;

    // ---------------------------------------------------------- Salmon base palette (legacy Tokens.Dark/Light.xaml)

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

    // ---------------------------------------------------------- Fixed semantic colors (success/warning/danger)

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

    // ---------------------------------------------------------- Color schemes (override the salmon base palette)

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

    /// <summary>Palettes generated by the theme workshop (registered on demand, persisted in themes.json).</summary>
    private static readonly List<ThemePalette> Custom = [];

    /// <summary>All palettes (built-in + workshop-generated).</summary>
    public static IReadOnlyList<ThemePalette> Palettes => [.. BuiltIn, .. Custom];

    /// <summary>Register (or overwrite) a custom palette.</summary>
    public static void RegisterPalette(ThemePalette palette)
    {
        Custom.RemoveAll(p => p.Id == palette.Id);
        Custom.Add(palette);
    }

    /// <summary>Three representative colors for palette previews (background / card / border).</summary>
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

    /// <summary>On-disk structure for workshop themes.</summary>
    private sealed record ThemeCache(List<ThemePalette> Palettes, List<AccentChoice> Accents);

    /// <summary>Restore workshop-generated themes from disk (called at startup; failures are silent).</summary>
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
            // A corrupted theme cache does not affect startup
        }
    }

    /// <summary>Write workshop-generated themes back to disk.</summary>
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
            // A write failure does not interrupt usage
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

    /// <summary>Register (or overwrite) an accent generated by the theme workshop.</summary>
    public static void RegisterAccent(AccentChoice accent)
    {
        CustomAccents.RemoveAll(a => a.Id == accent.Id);
        CustomAccents.Add(accent);
    }

    // ---------------------------------------------------------- Apply

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
            // Read failed: default to dark
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
            // Ignore follow-system refreshes when SystemEvents is unavailable
        }
    }

    /// <summary>Write the current palette into both theme dictionaries. Fully rewritten each time so no old colors remain.</summary>
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

        // When a wallpaper is present, keep the page background slightly transparent so the image shows through without hurting readability
        var pageDark = Parse(darkSrc.TryGetValue("Brush.PageBg", out var pd) ? pd : "#13191D");
        var pageLight = Parse(lightSrc.TryGetValue("Brush.PageBg", out var pl) ? pl : "#EDF1F2");
        Put(dark, "Brush.PageBgTrans", WithAlpha(pageDark, 0xF0));
        Put(light, "Brush.PageBgTrans", WithAlpha(pageLight, 0xF4));
        Put(dark, "Brush.WallpaperScrim", Argb(0x3D, 0, 0, 0));
        Put(light, "Brush.WallpaperScrim", Argb(0x2E, 0xFF, 0xFF, 0xFF));

        // Accent family (written into both dictionaries, with values depending on the theme variant)
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

        // Nav selection background follows the accent (unless the palette explicitly overrides it)
        if (!darkSrc.ContainsKey("Brush.NavItemChecked"))
            Put(dark, "Brush.NavItemChecked", WithAlpha(accentDark, 0x1F));
        if (!lightSrc.ContainsKey("Brush.NavItemChecked"))
            Put(light, "Brush.NavItemChecked", WithAlpha(accentLight, 0x1F));

        // Override the SystemAccentColor family -> native controls follow the custom accent
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

    /// <summary>Convert "#RRGGBB"/"#AARRGGBB" to a color (used for palette preview swatches).</summary>
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
