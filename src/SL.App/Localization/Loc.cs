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
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SL.App.Localization;

/// <summary>Selectable language.</summary>
public sealed record LanguageOption(string Code, string Name);

/// <summary>
/// Lightweight localization layer:
/// - Follows the system language by default (auto), but can be pinned to a specific language in settings;
/// - Maps using the Simplified Chinese source text as the key, so no resource key is needed per control;
/// - Switching languages replaces text in place across the whole tree (including switching back to the source text), without rebuilding pages.
/// </summary>
public static partial class Loc
{
    public const string AutoCode = "auto";
    public const string SourceLang = "zh-Hans";

    public static IReadOnlyList<LanguageOption> Languages { get; } =
    [
        new(AutoCode, "跟随系统"),
        new("zh-Hans", "简体中文"),
        new("zh-Hant", "繁體中文"),
        new("en", "English"),
        new("ja", "日本語"),
    ];

    /// <summary>Language setting (auto or a concrete language).</summary>
    public static string Setting { get; private set; } = AutoCode;

    /// <summary>The language actually in effect.</summary>
    public static string Current { get; private set; } = SourceLang;

    /// <summary>Language change notification (used to refresh the title bar, navigation, and menu).</summary>
    public static event Action? Changed;

    public static string DisplayName(string code)
        => Languages.FirstOrDefault(l => l.Code == code)?.Name ?? code;

    /// <summary>Initialize (called once at startup).</summary>
    public static void Initialize(string? setting)
    {
        Setting = NormalizeSetting(setting);
        Current = Resolve(Setting);
    }

    private static string NormalizeSetting(string? setting)
        => string.IsNullOrWhiteSpace(setting) ? AutoCode
            : Languages.Any(l => l.Code == setting) ? setting!
            : AutoCode;

    /// <summary>Resolve the setting into the effective language.</summary>
    public static string Resolve(string? setting)
    {
        var code = NormalizeSetting(setting);
        return code == AutoCode ? DetectSystem() : code;
    }

    /// <summary>System preferred languages -> supported language; falls back to English when unsupported.</summary>
    public static string DetectSystem()
    {
        try
        {
            foreach (var lang in Windows.System.UserProfile.GlobalizationPreferences.Languages)
            {
                var mapped = MapTag(lang);
                if (mapped is not null)
                    return mapped;
            }

            var primary = Windows.System.UserProfile.GlobalizationPreferences.Languages
                .FirstOrDefault() ?? "";
            return MapTag(primary, allowGeneric: true) ?? "en";
        }
        catch
        {
            return SourceLang;
        }
    }

    private static string? MapTag(string? tag, bool allowGeneric = false)
    {
        if (string.IsNullOrWhiteSpace(tag))
            return null;
        var t = tag.Replace('_', '-').ToLowerInvariant();

        if (t.StartsWith("zh"))
        {
            if (t.Contains("hant") || t.Contains("tw") || t.Contains("hk") || t.Contains("mo"))
                return "zh-Hant";
            if (t.Contains("hans") || t.Contains("cn") || t.Contains("sg") || t.Contains("my"))
                return "zh-Hans";
            return allowGeneric ? "zh-Hans" : null;
        }
        if (t.StartsWith("ja"))
            return "ja";
        if (t.StartsWith("en"))
            return "en";
        return null;
    }

    private static Dictionary<string, string>? MapOf(string lang) => lang switch
    {
        "en" => En,
        "ja" => Ja,
        _ => null,
    };

    /// <summary>Translate a Simplified Chinese source string (returns it unchanged if absent from the dictionary).</summary>
    public static string T(string source)
    {
        if (Current == SourceLang || string.IsNullOrEmpty(source))
            return source;
        if (Current == "zh-Hant")
            return ToHant(source);
        var map = MapOf(Current);
        return map is not null && map.TryGetValue(source, out var text) ? text : source;
    }

    // ---------------------------------------------------------------- In-place visual tree replacement

    /// <summary>Apply the current language to a loaded visual tree.</summary>
    public static void ApplyTo(DependencyObject? root)
    {
        if (root is null)
            return;
        Walk(root, Convert);
    }

    /// <summary>Switch language and update the whole tree in place (without rebuilding pages).</summary>
    public static void SwitchTo(string setting, DependencyObject? root)
    {
        var old = Current;
        var oldMap = MapOf(old);
        var oldHant = old == "zh-Hant";

        Setting = NormalizeSetting(setting);
        Current = Resolve(Setting);

        if (root is not null)
        {
            Walk(root, v =>
            {
                var zh = ToSource(v, old, oldMap, oldHant);
                return ToTarget(zh);
            });
        }

        Changed?.Invoke();
    }

    private static string ToSource(string value, string oldLang, Dictionary<string, string>? oldMap, bool oldHant)
    {
        if (oldLang == SourceLang || string.IsNullOrEmpty(value))
            return value;
        if (oldHant)
            return FromHant(value);
        if (oldMap is not null)
        {
            foreach (var kv in oldMap)
            {
                if (kv.Value == value)
                    return kv.Key;
            }
        }
        return value;
    }

    private static string ToTarget(string zh)
    {
        if (Current == SourceLang)
            return zh;
        if (Current == "zh-Hant")
            return ToHant(zh);
        var map = MapOf(Current);
        return map is not null && map.TryGetValue(zh, out var text) ? text : zh;
    }

    private static string Convert(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        if (Current == SourceLang)
            return value;
        return ToTarget(value);
    }

    private static void Walk(DependencyObject node, Func<string, string> convert)
    {
        switch (node)
        {
            case TextBlock tb when tb.Text is { Length: > 0 } text:
            {
                var next = convert(text);
                if (!ReferenceEquals(next, text) && next != text)
                    tb.Text = next;
                break;
            }
            case TextBox box when !string.IsNullOrEmpty(box.PlaceholderText):
            {
                var next = convert(box.PlaceholderText);
                if (next != box.PlaceholderText)
                    box.PlaceholderText = next;
                break;
            }
            case ToggleSwitch toggle:
            {
                if (toggle.OnContent is string on && on.Length > 0)
                {
                    var next = convert(on);
                    if (next != on)
                        toggle.OnContent = next;
                }
                if (toggle.OffContent is string off && off.Length > 0)
                {
                    var next = convert(off);
                    if (next != off)
                        toggle.OffContent = next;
                }
                break;
            }
            case ContentControl cc when cc.Content is string content && content.Length > 0:
            {
                var next = convert(content);
                if (next != content)
                    cc.Content = next;
                break;
            }
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
            Walk(VisualTreeHelper.GetChild(node, i), convert);
    }

    /// <summary>Whether the current language's text is translated (for self-checks).</summary>
    public static int TranslatedCount => Current switch
    {
        "en" => En.Count,
        "ja" => Ja.Count,
        _ => 0,
    };
}
