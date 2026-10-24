using SL.Core.Java;

namespace SL.App.Infrastructure;

/// <summary>
/// Java 环境状态:应用启动时后台扫描一次,结果供启动面板 / 实例概览展示。
/// 异步推送通过 Changed 事件触发绑定刷新,不阻塞启动。
/// </summary>
public static class JavaEnv
{
    private static string _statusText = "Java:扫描中…";
    private static string _detailText = "";

    /// <summary>一句话状态(启动面板显示)。</summary>
    public static string StatusText
    {
        get => _statusText;
        private set
        {
            if (!ReferenceEquals(_statusText, value) && _statusText != value)
            {
                _statusText = value;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>附加详情(首选路径 / 数量)。</summary>
    public static string DetailText
    {
        get => _detailText;
        private set
        {
            _detailText = value;
            Changed?.Invoke();
        }
    }

    /// <summary>最近一次扫描到的最佳运行时(启动流程使用)。</summary>
    public static JavaInstall? Best { get; private set; }

    public static event Action? Changed;

    /// <summary>启动时扫描一次(不抛出)。</summary>
    public static async void Warm() => await RescanAsync().ConfigureAwait(true);

    /// <summary>重新扫描(自动装完 JDK / 游戏退出后调用,状态推给绑定)。不抛出。</summary>
    public static async Task RescanAsync()
    {
        try
        {
            var list = await JavaScan.ScanAsync(CancellationToken.None).ConfigureAwait(true);
            if (list.Count == 0)
            {
                StatusText = "未检测到本地 Java(启动游戏时按需自动获取)";
                DetailText = "";
                Best = null;
                Changed?.Invoke();
                return;
            }

            Best = list[0];
            StatusText = $"Java {Best.VersionText} · {Best.Vendor}";
            DetailText = list.Count == 1 ? Best.HomePath : $"共 {list.Count} 个运行时 · 首选 {Best.HomePath}";
        }
        catch (Exception ex)
        {
            StatusText = "Java 扫描失败:" + ex.Message;
            Core.Logging.AppLog.Error("JavaEnv.Rescan", ex);
        }
        Changed?.Invoke();
    }
}
