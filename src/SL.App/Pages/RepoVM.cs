using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SL.Core.Instances;
using SL.Core.Logging;
using SL.Core.Net;
using SL.App.Download;
using SL.App.Dialogs;
using SL.App.Infrastructure;
using SL.App.Notify;

namespace SL.App.Pages;

/// <summary>Modrinth 项目卡片行。</summary>
public sealed class RepoRow
{
    public RepoRow(Modrinth.ProjectHit hit, RepoVM owner)
    {
        Hit = hit;
        InstallCommand = new RelayCommand(async () => await owner.InstallAsync(this));
    }

    public Modrinth.ProjectHit Hit { get; }
    public RelayCommand InstallCommand { get; }

    public string Title => Hit.Title;
    public string Description => Hit.Description;
    public string Author => "作者 " + Hit.Author;
    public string Downloads => "下载 " + Hit.DownloadsText;
    public string Categories => string.Join(" · ", Hit.Categories);
}

/// <summary>仓库页视图模型:模组 / 光影 / 资源包三类共用,检索与安装均走真实网络。</summary>
public sealed class RepoVM : VmBase, IRefreshable
{
    private readonly DispatcherQueue _ui = DispatcherQueue.GetForCurrentThread();
    private ModrinthCategory _category = ModrinthCategory.Mods;
    private bool _loadedOnce;

    public RepoVM()
    {
        SearchCommand = new RelayCommand(async () => await SearchAsync());
    }

    public ObservableCollection<RepoRow> Rows { get; } = [];

    public RelayCommand SearchCommand { get; }

    private string _keyword = "";
    public string Keyword
    {
        get => _keyword;
        set => Set(ref _keyword, value);
    }

    private string _title = "模组仓库";
    public string Title { get => _title; private set => Set(ref _title, value); }

    private string _status = "";
    public string Status { get => _status; private set => Set(ref _status, value); }

    private bool _busy;
    public bool Busy
    {
        get => _busy;
        private set
        {
            if (Set(ref _busy, value))
                On(nameof(BusyVisibility));
        }
    }

    public Visibility BusyVisibility => Busy ? Visibility.Visible : Visibility.Collapsed;

    public string TargetText { get; private set; } = "";

    private string _targetInstance = "";

    public string SearchHint => _category switch
    {
        ModrinthCategory.Mods => "搜索模组(如 sodium、create)",
        ModrinthCategory.Shaders => "搜索光影(如 complementary、bsl)",
        _ => "搜索资源包(如 faithful、stay true)",
    };

    public string SubDir => _category switch
    {
        ModrinthCategory.Mods => "mods",
        ModrinthCategory.Shaders => "shaderpacks",
        _ => "resourcepacks",
    };

    /// <summary>路由 key 决定分类:dl.pack / dl.shaders / dl.packs。</summary>
    public void Configure(string routeKey)
    {
        _category = routeKey switch
        {
            "dl.shaders" => ModrinthCategory.Shaders,
            "dl.packs" => ModrinthCategory.ResourcePacks,
            _ => ModrinthCategory.Mods,
        };
        Title = _category switch
        {
            ModrinthCategory.Mods => "模组仓库",
            ModrinthCategory.Shaders => "光影仓库",
            _ => "资源包仓库",
        };
        On(nameof(SearchHint));
        On(nameof(TargetText));
    }

    public void RefreshData()
    {
        var latest = InstanceRegistry.Latest();
        _targetInstance = latest?.InstanceId ?? "";
        TargetText = latest is null
            ? "尚未安装实例 · 下载后将保存到实例目录,请先安装一个版本"
            : $"安装目标:实例 {latest.InstanceId} → {SubDir}";

        if (!_loadedOnce)
        {
            _loadedOnce = true;
            _ = SearchAsync();
        }
    }

    private async Task SearchAsync()
    {
        if (Busy)
            return;
        Busy = true;
        Status = "正在检索 Modrinth…";
        try
        {
            var gameVersion = _targetInstance.Length > 0
                ? _targetInstance
                : "1.21.4";

            IReadOnlyList<Modrinth.ProjectHit> hits;
            if (_category == ModrinthCategory.Mods)
            {
                var facets = $"[[\"versions:{gameVersion}\"]]";
                hits = await Modrinth.SearchByFacetsAsync(_keyword, facets, CancellationToken.None).ConfigureAwait(true);
            }
            else
            {
                var type = _category == ModrinthCategory.Shaders ? "shader" : "resourcepack";
                var facets = $"[[\"project_type:{type}\"],[\"versions:{gameVersion}\"]]";
                hits = await Modrinth.SearchByFacetsAsync(_keyword, facets, CancellationToken.None).ConfigureAwait(true);
            }

            Rows.Clear();
            foreach (var hit in hits)
                Rows.Add(new RepoRow(hit, this));

            Status = hits.Count == 0
                ? "没有匹配结果 · 换个关键词或确认实例版本"
                : $"共 {hits.Count} 个结果 · 版本过滤 {gameVersion}";
        }
        catch (Exception ex)
        {
            AppLog.Error("RepoPage", ex);
            Status = "检索失败:" + ex.Message;
            AppServices.Notify.Publish(NotifyKind.Error, "检索失败:" + ex.Message);
        }
        finally
        {
            Busy = false;
        }
    }

    internal async Task InstallAsync(RepoRow row)
    {
        if (_targetInstance.Length == 0)
        {
            await AppServices.Dialogs.AlertAsync("尚未安装实例",
                "请先在「下载 → 游戏版本」安装一个实例;模组、光影与资源包都需要放进某个实例目录。",
                MsgMark.Info).ConfigureAwait(true);
            return;
        }

        try
        {
            Status = $"正在解析 {row.Title} 的最新文件…";
            var file = _category == ModrinthCategory.Mods
                ? await Modrinth.LatestVersionFileAsync(row.Hit.Slug, _targetInstance, "fabric", CancellationToken.None)
                    .ConfigureAwait(true)
                : await Modrinth.LatestVersionFileAsync(row.Hit.Slug, CancellationToken.None).ConfigureAwait(true);

            if (file is null)
            {
                Status = $"{row.Title} 没有适用于 {_targetInstance} 的文件";
                AppServices.Notify.Publish(NotifyKind.Warning, $"{row.Title} 没有适用于当前版本的文件");
                return;
            }

            InstallWizard.SubmitInstanceFile(_targetInstance, SubDir, file.FileName,
                file.Url, file.Sha1, file.Size);
            AppLog.Info("RepoPage", $"已提交 {row.Title} → {SubDir}/{file.FileName}");
            Status = $"已提交 {file.FileName}({file.SizeText})到 {SubDir}";
            AppServices.Notify.Publish(NotifyKind.Success, $"已提交下载:{file.FileName}");
        }
        catch (Exception ex)
        {
            AppLog.Error("RepoPage", ex);
            Status = "安装失败:" + ex.Message;
            AppServices.Notify.Publish(NotifyKind.Error, "安装失败:" + ex.Message);
        }
    }
}