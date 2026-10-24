using System.Windows.Input;
using SL.Pres.Infrastructure;

namespace SL.Pres.Dialogs;

public partial class DialogMsgView : DialogPanel
{
    private string _title = "";
    private string _message = "";
    private string _kindKey = "Question";
    private string _glyph = "?";
    private bool _askCancel;
    private string _okLabel = "确定";
    private string _cancelLabel = "取消";

    public string Title { get => _title; set { _title = value; OnChanged(); } }
    public string Message { get => _message; set { _message = value; OnChanged(); } }
    public string KindKey { get => _kindKey; private set { _kindKey = value; OnChanged(); } }
    public string Glyph { get => _glyph; private set { _glyph = value; OnChanged(); } }
    public bool AskCancel { get => _askCancel; private set { _askCancel = value; OnChanged(); } }
    public string OkLabel { get => _okLabel; set { _okLabel = value; OnChanged(); } }
    public string CancelLabel { get => _cancelLabel; set { _cancelLabel = value; OnChanged(); } }

    public ICommand ConfirmCommand { get; private set; } = null!;
    public ICommand CancelCommand { get; private set; } = null!;

    public DialogMsgView()
    {
        InitializeComponent();
        ConfirmCommand = new RelayCommand(() => Complete(true));
        CancelCommand = new RelayCommand(() => Complete(false));
        DataContext = this;
    }

    public void Configure(string title, string message, MsgMark mark, bool askCancel, string? okLabel, string? cancelLabel)
    {
        Title = title;
        Message = message;
        KindKey = mark.ToString();
        Glyph = mark switch
        {
            MsgMark.Success => "✔",
            MsgMark.Warning => "⚠",
            MsgMark.Error => "✖",
            MsgMark.Question => "?",
            _ => "ℹ",
        };
        AskCancel = askCancel;
        OkLabel = okLabel ?? "确定";
        CancelLabel = cancelLabel ?? "取消";
    }

    protected override void OnCanceled() => Complete(false);
}
