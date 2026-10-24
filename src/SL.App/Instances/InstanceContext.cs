using SL.Core.Instances;

namespace SL.App.Instances;

/// <summary>
/// 当前实例上下文:实例相关页面共用同一个「正在查看的实例」,
/// 切换后所有页面同步刷新,默认取最近安装的实例。
/// </summary>
public static class InstanceContext
{
    public static string CurrentId { get; private set; } = "";

    public static event Action? Changed;

    public static IReadOnlyList<InstanceInfo> All => InstanceRegistry.List();

    /// <summary>刷新实例列表并保证 CurrentId 有效(失效时回落到最新实例)。</summary>
    public static IReadOnlyList<InstanceInfo> Refresh()
    {
        var all = InstanceRegistry.List();
        if (all.All(i => i.InstanceId != CurrentId))
        {
            CurrentId = all.Count > 0 ? all[0].InstanceId : "";
            Changed?.Invoke();
        }
        return all;
    }

    public static void Select(string instanceId)
    {
        if (CurrentId == instanceId)
            return;
        CurrentId = instanceId;
        Changed?.Invoke();
    }
}