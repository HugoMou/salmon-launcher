using System.Windows;

namespace SL.Pres.Navigation;

/// <summary>一个可导航页面的静态描述。左侧面板与主内容都按需延迟构建并按 Key 缓存。</summary>
public sealed class PageSpec
{
    public required string Key { get; init; }
    public required string Title { get; init; }

    /// <summary>层级:0 = 分区根页,1 及更深 = 下钻页(触发入栈与返回按钮)</summary>
    public int Level { get; init; }

    public required Func<UIElement> BuildLeft { get; init; }
    public required Func<UIElement> BuildContent { get; init; }
}

public static class PageCatalog
{
    private static readonly Dictionary<string, PageSpec> Pages = new(StringComparer.Ordinal);

    public static void Add(PageSpec spec) => Pages[spec.Key] = spec;

    public static PageSpec Get(string key) => Pages.TryGetValue(key, out var spec)
        ? spec
        : throw new KeyNotFoundException($"未注册页面:{key}");

    public static IReadOnlyDictionary<string, PageSpec> All => Pages;

    /// <summary>构造并缓存某个页面的左侧面板。</summary>
    private static readonly Dictionary<string, UIElement> LeftCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, UIElement> ContentCache = new(StringComparer.Ordinal);

    public static UIElement ContentFor(string key)
    {
        var spec = Get(key);
        if (ContentCache.TryGetValue(key, out var cached))
        {
            // 页面常驻;进入时刷新其数据(模组安装/备份完成等磁盘事实变化可即时呈现)
            if (cached is System.Windows.FrameworkElement fe && fe.DataContext is Infrastructure.IRefreshable r)
                r.RefreshData();
            return cached;
        }
        var view = spec.BuildContent();
        ContentCache[key] = view;
        return view;
    }

    public static UIElement LeftFor(string key)
    {
        if (LeftCache.TryGetValue(key, out var cached))
            return cached;
        var view = Get(key).BuildLeft();
        LeftCache[key] = view;
        return view;
    }
}
