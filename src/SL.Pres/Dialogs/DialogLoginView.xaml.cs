using System.Windows;
using System.Windows.Input;
using SL.Pres.Infrastructure;

namespace SL.Pres.Dialogs;

public partial class DialogLoginView : DialogPanel
{
    private string _mode = "offline";
    private string _displayName = "";

    public string Mode { get => _mode; private set { _mode = value; OnChanged(); } }
    public string DisplayName { get => _displayName; set { _displayName = value; OnChanged(); RefreshCommands(); } }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public DialogLoginView()
    {
        InitializeComponent();
        DataContext = this;

        CancelCommand = new RelayCommand(() => Complete(null));
        ConfirmCommand = new RelayCommand(OnConfirm, CanConfirm);

        PasswordBox.PasswordChanged += (_, _) => RefreshCommands();
    }

    private void OnConfirm()
    {
        var name = DisplayName.Trim();
        if (name.Length == 0)
            return;

        // 阶段一:仅离线账户的界面演示结果。
        if (Mode == "offline")
            Complete(new LoginDraftResult(name, OfflineMode: true));
    }

    private bool CanConfirm()
        => Mode == "offline" && DisplayName.Trim().Length > 0;

    private void RefreshCommands() => CommandManager.InvalidateRequerySuggested();

    protected override void OnCanceled() => Complete(null);
}
