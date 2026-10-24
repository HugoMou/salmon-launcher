using System.Windows;
using System.Windows.Input;
using SL.Pres.Infrastructure;

namespace SL.Pres.Dialogs;

public partial class DialogInputView : DialogPanel
{
    private string _title = "";
    private string _message = "";
    private string _inputText = "";

    public string Title { get => _title; private set { _title = value; OnChanged(); } }
    public string Message { get => _message; private set { _message = value; OnChanged(); } }

    public string InputText
    {
        get => _inputText;
        set
        {
            if (_inputText == value)
                return;
            _inputText = value;
            OnChanged();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public DialogInputView()
    {
        InitializeComponent();
        DataContext = this;

        CancelCommand = new RelayCommand(() => Complete(null));
        ConfirmCommand = new RelayCommand(
            () => Complete(InputText),
            () => InputText.Trim().Length > 0);

        Loaded += (_, _) =>
        {
            var box = this.FindFirstChild<System.Windows.Controls.TextBox>();
            box?.Focus();
            box?.SelectAll();
        };
    }

    public void Configure(string title, string hint, string initial)
    {
        Title = title;
        Message = hint;
        InputText = initial;
    }

    protected override void OnCanceled() => Complete(null);
}
