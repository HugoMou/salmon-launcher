namespace SL.Pres.Infrastructure;

/// <summary>
/// 页面在导航间常驻(缓存),数据可能在别处变化(装好模组/备份完成等)。
/// 实现 Data() 让 PageCatalog 在每次进入该页时刷新展示,而不是重建视图(避免 VM 泄漏与状态丢失)。
/// </summary>
public interface IRefreshable
{
    /// <summary>进入页面时调用(在栈下或返回时均触发);要求轻量、同步。</summary>
    void RefreshData();
}
