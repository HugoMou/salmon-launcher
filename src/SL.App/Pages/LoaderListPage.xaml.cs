using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SL.App.Download;
using SL.App.Infrastructure;
using SL.App.Notify;

namespace SL.App.Pages;

/// <summary>加载器条目。</summary>
public sealed class LoaderRow
{
    public LoaderRow(string name, string tag, string detail, string glyph, bool available, Action choose)
    {
        Name = name;
        Tag = tag;
        Detail = detail;
        Glyph = glyph;
        Available = available;
        ChooseCommand = new RelayCommand(choose);
    }

    public string Name { get; }
    public string Tag { get; }
    public string Detail { get; }
    public string Glyph { get; }
    public bool Available { get; }
    public RelayCommand ChooseCommand { get; }
}

/// <summary>加载器选择页:原版可直接安装,其余加载器在接入官方安装元数据前保持禁用。</summary>
public sealed partial class LoaderListPage : Page
{
    public LoaderListPage()
    {
        InitializeComponent();
        LoaderList.ItemsSource = BuildRows();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        SummaryText.Text = InstallWizard.HasVersion
            ? $"Minecraft {InstallWizard.VersionId} · 选择要一起安装的加载器"
            : "尚未选择游戏版本 · 请先回到「下载 → 游戏版本」选择一个版本";
    }

    private static IReadOnlyList<LoaderRow> BuildRows() =>
    [
        new("原版(纯净)", "推荐", "只安装官方核心、库与资源;可随时再加模组。", "\uE7FC", true,
            () => Choose("原版")),
        new("Fabric", "模组", "轻量模组加载器。当前版本暂不支持一键安装,可先安装原版再用官方安装器。", "\uE7B8", false,
            () => { }),
        new("NeoForge", "模组", "1.20.2+ 的社区分支。当前版本暂不支持一键安装。", "\uE7B8", false,
            () => { }),
        new("Forge", "模组", "经典模组加载器。当前版本暂不支持一键安装。", "\uE7B8", false,
            () => { }),
        new("Quilt", "模组", "Fabric 衍生加载器。当前版本暂不支持一键安装。", "\uE7B8", false,
            () => { }),
    ];

    private static void Choose(string loader)
    {
        InstallWizard.ChooseLoader(loader);
        AppServices.Notify.Publish(NotifyKind.Info, $"加载器:{loader} · 进入安装概览");
        AppServices.Nav.Navigate("dl.install");
    }
}