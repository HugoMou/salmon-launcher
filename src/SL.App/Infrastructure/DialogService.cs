using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace SL.App.Dialogs;

/// <summary>提示口味(信息/成功/警告/错误/询问)。</summary>
public enum MsgMark
{
    Info,
    Success,
    Warning,
    Error,
    Question,
}

/// <summary>
/// 模态对话框服务(WinUI 3 ContentDialog):
/// - 同一 XamlRoot 同时只能有一个 ContentDialog,内部用信号量排队;
/// - API 与旧版(WPF 自绘层)保持一致,便于页面平移。
/// </summary>
public sealed class DialogService
{
    private XamlRoot? _root;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>由 MainWindow 注入根 XamlRoot。</summary>
    public void Attach(XamlRoot root) => _root = root;

    // ============================== 常用对话框 ==============================

    public async Task<bool> ConfirmAsync(string title, string message, MsgMark mark = MsgMark.Question,
        string? okLabel = null, string? cancelLabel = null)
    {
        var dialog = Build(title, MessageContent(message, mark),
            okLabel ?? "确定", cancelLabel ?? "取消");
        var result = await ShowAsync(dialog).ConfigureAwait(true);
        return result == ContentDialogResult.Primary;
    }

    public async Task<bool> AlertAsync(string title, string message, MsgMark mark = MsgMark.Info)
    {
        var dialog = Build(title, MessageContent(message, mark), "知道了", null);
        await ShowAsync(dialog).ConfigureAwait(true);
        return true;
    }

    public async Task<string?> PromptAsync(string title, string? hint, string initial = "")
    {
        var input = new TextBox
        {
            Text = initial,
            PlaceholderText = hint ?? "",
            SelectionStart = initial.Length,
        };
        var panel = new StackPanel { Spacing = 12 };
        if (!string.IsNullOrWhiteSpace(hint))
            panel.Children.Add(new TextBlock
            {
                Text = hint,
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap,
            });
        panel.Children.Add(input);

        var dialog = Build(title, panel, "确定", "取消");
        var result = await ShowAsync(dialog).ConfigureAwait(true);
        if (result != ContentDialogResult.Primary)
            return null;
        var text = input.Text;
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public async Task<int?> ChooseAsync(string title, string? hint, IReadOnlyList<string> options)
    {
        var list = new RadioButtons
        {
            ItemsSource = options,
            MaxColumns = 1,
            SelectedIndex = 0,
        };
        var panel = new StackPanel { Spacing = 12 };
        if (!string.IsNullOrWhiteSpace(hint))
            panel.Children.Add(new TextBlock
            {
                Text = hint,
                Opacity = 0.72,
                TextWrapping = TextWrapping.Wrap,
            });
        panel.Children.Add(list);

        var dialog = Build(title, panel, "确定", "取消");
        var result = await ShowAsync(dialog).ConfigureAwait(true);
        return result == ContentDialogResult.Primary ? list.SelectedIndex : null;
    }

    // ============================== 构建与调度 ==============================

    private static UIElement MessageContent(string message, MsgMark mark)
    {
        var glyph = mark switch
        {
            MsgMark.Success => "\uE73E",
            MsgMark.Warning => "\uE7BA",
            MsgMark.Error => "\uE783",
            MsgMark.Question => "\uE897",
            _ => "\uE946",
        };
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 14,
        };
        panel.Children.Add(new FontIcon
        {
            Glyph = glyph,
            FontSize = 22,
            VerticalAlignment = VerticalAlignment.Top,
        });
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return panel;
    }

    private ContentDialog Build(string title, UIElement content, string primary, string? close)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primary,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = _root ?? throw new InvalidOperationException("对话框服务尚未初始化(Attach 未调用)"),
        };
        if (close is not null)
            dialog.CloseButtonText = close;
        return dialog;
    }

    /// <summary>同一时刻只展示一个对话框;后续请求排队依次弹出。</summary>
    private async Task<ContentDialogResult> ShowAsync(ContentDialog dialog)
    {
        await _gate.WaitAsync().ConfigureAwait(true);
        try
        {
            return await dialog.ShowAsync().AsTask().ConfigureAwait(true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
