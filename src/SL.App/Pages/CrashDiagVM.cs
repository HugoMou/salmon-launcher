using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using SL.Core.Logging;
using SL.Core.Tools;
using SL.App.Infrastructure;

namespace SL.App.Pages;

/// <summary>可诊断文件行。</summary>
public sealed class CrashRow
{
    public CrashRow(string path, DateTimeOffset time)
    {
        Path = path;
        Name = System.IO.Path.GetFileName(path);
        Detail = $"{time:yyyy-MM-dd HH:mm} · {System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(path))}";
    }

    public string Path { get; }
    public string Name { get; }
    public string Detail { get; }
}

/// <summary>崩溃诊断页视图模型:扫描实例崩溃报告与日志,按规则表给出结论。</summary>
public sealed class CrashDiagVM : VmBase, IRefreshable
{
    private CrashRow? _selectedFile;
    private string _summary = "正在扫描…";
    private string _excerpt = "";

    public CrashDiagVM()
    {
        RescanCommand = new RelayCommand(RefreshData);
    }

    public ObservableCollection<CrashRow> Files { get; } = [];
    public ObservableCollection<string> Findings { get; } = [];

    public RelayCommand RescanCommand { get; }

    public CrashRow? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (Set(ref _selectedFile, value))
                Analyze();
        }
    }

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public string Excerpt { get => _excerpt; private set => Set(ref _excerpt, value); }

    public string FindingsEmptyHint => Files.Count == 0
        ? "未发现可诊断文件。游戏崩溃后会在实例目录生成 crash-reports,这里会自动列出。"
        : "未命中已收录的崩溃特征。可对照下方报告摘录排查,或到「实例 → 实例日志」查看完整日志。";

    public Visibility FindingsEmptyVisibility
        => Findings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public void RefreshData()
    {
        try
        {
            var rows = CrashDiagnostics.ListReadable()
                .Select(x => new CrashRow(x.Path, x.Time))
                .ToList();
            var keep = _selectedFile?.Path;
            Files.Clear();
            foreach (var row in rows)
                Files.Add(row);

            Summary = rows.Count == 0
                ? "未发现崩溃报告 · 游戏正常运行时不产生报告"
                : $"共 {rows.Count} 个可诊断文件 · 新→旧排列";

            SelectedFile = Files.FirstOrDefault(f => f.Path == keep) ?? Files.FirstOrDefault();
            if (SelectedFile is null)
            {
                Findings.Clear();
                Excerpt = "";
                On(nameof(FindingsEmptyVisibility));
                On(nameof(FindingsEmptyHint));
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("CrashDiagPage", ex);
            Summary = "扫描失败:" + ex.Message;
        }
    }

    private void Analyze()
    {
        Findings.Clear();
        if (_selectedFile is null)
        {
            Excerpt = "";
            On(nameof(FindingsEmptyVisibility));
            On(nameof(FindingsEmptyHint));
            return;
        }

        try
        {
            var result = CrashDiagnostics.Analyze(_selectedFile.Path);
            foreach (var finding in result.Findings)
                Findings.Add(finding);
            Excerpt = result.Excerpt;
        }
        catch (Exception ex)
        {
            AppLog.Error("CrashDiagPage", ex);
            Excerpt = "分析失败:" + ex.Message;
        }

        On(nameof(FindingsEmptyVisibility));
        On(nameof(FindingsEmptyHint));
    }
}