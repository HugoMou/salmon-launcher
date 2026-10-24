using System.Collections.ObjectModel;
using System.Diagnostics;
using SL.Core.Boot;
using SL.Core.Logging;
using SL.Core.Tools;
using SL.App.Infrastructure;

namespace SL.App.Pages;

/// <summary>日志文件行。</summary>
public sealed class LogFileRow
{
    public LogFileRow(string path)
    {
        Path = path;
        Display = System.IO.Path.GetFileName(path);
    }

    public string Path { get; }
    public string Display { get; }
}

/// <summary>应用日志页视图模型:文件选择、关键词过滤与尾部读取。</summary>
public sealed class AppLogVM : VmBase, IRefreshable
{
    private const int TailLines = 2000;

    private string _logText = "";
    private string _summary = "正在读取…";
    private bool _errorsOnly;

    public AppLogVM()
    {
        RefreshCommand = new RelayCommand(RefreshData);
        OpenDirCommand = new RelayCommand(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Runtime.LogDir,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                AppLog.Warn("AppLogPage", "打开日志目录失败:" + ex.Message);
            }
        });
    }

    public ObservableCollection<LogFileRow> Files { get; } = [];

    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenDirCommand { get; }

    private LogFileRow? _selectedFile;
    public LogFileRow? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (Set(ref _selectedFile, value))
                Load();
        }
    }

    private string _keyword = "";
    public string Keyword
    {
        get => _keyword;
        set
        {
            if (Set(ref _keyword, value))
                Load();
        }
    }

    public bool ErrorsOnly
    {
        get => _errorsOnly;
        set
        {
            if (Set(ref _errorsOnly, value))
                Load();
        }
    }

    public string LogText { get => _logText; private set => Set(ref _logText, value); }

    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    public void RefreshData()
    {
        var files = LogReader.LauncherLogs().Select(p => new LogFileRow(p)).ToList();
        var keep = _selectedFile?.Path;
        Files.Clear();
        foreach (var file in files)
            Files.Add(file);

        SelectedFile = Files.FirstOrDefault(f => f.Path == keep) ?? Files.FirstOrDefault();
        if (SelectedFile is null)
        {
            LogText = "尚无日志文件。启动器运行并产生事件后,日志会自动写入此处。";
            Summary = $"日志目录:{Runtime.LogDir}";
        }
    }

    private void Load()
    {
        if (_selectedFile is null)
            return;

        var keyword = _keyword.Trim();
        if (_errorsOnly)
            keyword = "[ERROR]";

        var tail = LogReader.ReadTail(_selectedFile.Path, TailLines, keyword);
        if (tail is null)
        {
            LogText = "无法读取该日志(文件可能已被删除或正被占用)。";
            Summary = _selectedFile.Display;
            return;
        }

        var text = string.Join(Environment.NewLine, tail.Lines);
        if (_errorsOnly && !string.IsNullOrWhiteSpace(_keyword.Trim()))
            text = string.Join(Environment.NewLine,
                tail.Lines.Where(l => l.Contains(_keyword.Trim(), StringComparison.OrdinalIgnoreCase)));

        LogText = string.IsNullOrWhiteSpace(text) ? "没有匹配的日志行。" : text;
        Summary = $"{_selectedFile.Display} · {tail.Lines.Count} 行 · 警告 {tail.WarnCount} · 错误 {tail.ErrorCount}";
    }
}