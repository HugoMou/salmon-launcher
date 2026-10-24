using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using SL.Pres.Infrastructure;

namespace SL.Pres.Panels;

/// <summary>左侧启动分区:当前实例卡片(真实数据,订阅 Java 状态推送)。</summary>
public sealed class LaunchPanelLeftVM : INotifyPropertyChanged
{
    private string _instanceName = "尚未安装版本";
    private string _badge = "去「下载 → 游戏版本」安装";
    private string _javaLine = "Java:扫描中…";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string InstanceName { get => _instanceName; private set { _instanceName = value; OnChanged(); } }
    public string Badge { get => _badge; private set { _badge = value; OnChanged(); } }
    public string JavaLine { get => _javaLine; private set { _javaLine = value; OnChanged(); } }

    public string AccountLine { get; private set; } = "离线账户 · …";

    private void OnChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name!));

    public void Refresh()
    {
        var inst = Core.Instances.InstanceRegistry.Latest();
        if (inst is null)
        {
            InstanceName = "尚未安装版本";
            Badge = "去「下载 → 游戏版本」安装";
        }
        else
        {
            InstanceName = inst.InstanceId;
            Badge = "已安装 · 磁盘事实";
        }
        JavaLine = $"{JavaEnv.StatusText} · 内存 4096 MB";
        AccountLine = "离线账户 · " + AppServices.Settings.Current.AccountName;
    }
}

/// <summary>启动分区左栏(数据驱动真实实例卡片)。</summary>
public partial class LaunchPanelLeft : UserControl
{
    public static readonly DependencyProperty UseTasksHintProperty =
        DependencyProperty.Register(nameof(UseTasksHint), typeof(bool), typeof(LaunchPanelLeft),
            new PropertyMetadata(false));

    /// <summary>任务中心页时显示提示卡。</summary>
    public bool UseTasksHint
    {
        get => (bool)GetValue(UseTasksHintProperty);
        set => SetValue(UseTasksHintProperty, value);
    }

    public LaunchPanelLeft(bool useTasksHint = false)
    {
        InitializeComponent();
        UseTasksHint = useTasksHint;
        var vm = new LaunchPanelLeftVM();
        DataContext = vm;
        // 初始即取当前事实;设置/Java 状态变化时刷新
        vm.Refresh();
        Infrastructure.JavaEnv.Changed += () => vm.Refresh();
        AppServices.Settings.Changed += _ => vm.Refresh();
    }
}
