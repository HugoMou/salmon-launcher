using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SL.App.Localization;

/// <summary>可选语言。</summary>
public sealed record LanguageOption(string Code, string Name);

/// <summary>
/// 轻量本地化层:
/// - 默认跟随系统语言(auto),也可在设置里固定为某种语言;
/// - 以「简体中文原文」为键做映射,无需给每个控件起资源键;
/// - 切换语言时整树就地替换文本(含再次切回原文),页面不需要重建。
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

    /// <summary>设置里的语言项(auto 或具体语言)。</summary>
    public static string Setting { get; private set; } = AutoCode;

    /// <summary>实际生效的语言。</summary>
    public static string Current { get; private set; } = SourceLang;

    /// <summary>语言变化通知(用于刷新标题栏、导航与菜单)。</summary>
    public static event Action? Changed;

    public static string DisplayName(string code)
        => Languages.FirstOrDefault(l => l.Code == code)?.Name ?? code;

    /// <summary>初始化(启动时调用一次)。</summary>
    public static void Initialize(string? setting)
    {
        Setting = NormalizeSetting(setting);
        Current = Resolve(Setting);
    }

    private static string NormalizeSetting(string? setting)
        => string.IsNullOrWhiteSpace(setting) ? AutoCode
            : Languages.Any(l => l.Code == setting) ? setting!
            : AutoCode;

    /// <summary>把设置项解析成实际语言。</summary>
    public static string Resolve(string? setting)
    {
        var code = NormalizeSetting(setting);
        return code == AutoCode ? DetectSystem() : code;
    }

    /// <summary>系统首选语言 → 支持的语言;不支持时回退英文。</summary>
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

    /// <summary>翻译一个简体中文原文(字典里没有则原样返回)。</summary>
    public static string T(string source)
    {
        if (Current == SourceLang || string.IsNullOrEmpty(source))
            return source;
        if (Current == "zh-Hant")
            return ToHant(source);
        var map = MapOf(Current);
        return map is not null && map.TryGetValue(source, out var text) ? text : source;
    }

    // ---------------------------------------------------------------- 视觉树就地替换

    /// <summary>对一棵已加载的可视树应用当前语言。</summary>
    public static void ApplyTo(DependencyObject? root)
    {
        if (root is null)
            return;
        Walk(root, Convert);
    }

    /// <summary>切换语言并就地更新整棵树(不重建页面)。</summary>
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

    /// <summary>当前语言的文案是否已翻译(供自检)。</summary>
    public static int TranslatedCount => Current switch
    {
        "en" => En.Count,
        "ja" => Ja.Count,
        _ => 0,
    };
}
