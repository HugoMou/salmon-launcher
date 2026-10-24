using System.ComponentModel;
using System.Windows.Input;
using SL.Pres.Infrastructure;

namespace SL.Pres.Dialogs;

public sealed class DialogOptionRow : VmBase
{
    private bool _isSelected;

    public string Name { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public DialogOptionRow(string name) => Name = name;
}

public partial class DialogSelectView : DialogPanel
{
    private string _title = "";
    private string _hint = "";
    private IReadOnlyList<DialogOptionRow> _options = [];

    public string Title { get => _title; private set { _title = value; OnChanged(); } }
    public string Hint { get => _hint; private set { _hint = value; OnChanged(); } }
    public IReadOnlyList<DialogOptionRow> Options { get => _options; private set { _options = value; OnChanged(); } }

    public ICommand ConfirmCommand { get; }
    public ICommand CancelCommand { get; }

    public DialogSelectView()
    {
        InitializeComponent();
        DataContext = this;

        CancelCommand = new RelayCommand(() => Complete(null));

        ConfirmCommand = new RelayCommand(
            () =>
            {
                var index = SelectedIndex();
                if (index >= 0)
                    Complete(index);
            },
            () => SelectedIndex() >= 0);
    }

    public void Configure(string title, string hint, IReadOnlyList<string> options)
    {
        Title = title;
        Hint = hint;

        var rows = options.Select(o => new DialogOptionRow(o)).ToList();
        foreach (var row in rows)
            row.PropertyChanged += OnRowChanged;

        Options = rows;
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DialogOptionRow.IsSelected))
            CommandManager.InvalidateRequerySuggested();
    }

    private int SelectedIndex()
    {
        for (var i = 0; i < _options.Count; i++)
        {
            if (_options[i].IsSelected)
                return i;
        }
        return -1;
    }

    protected override void OnCanceled() => Complete(null);
}
