using Makesense.Desktop.Commands;

namespace Makesense.Desktop.ViewModels.Dialogs;

public sealed class BulkLabelImportDialogViewModel : ModalDialogViewModel
{
    private readonly Func<string[]?> _loadFromFile;
    private readonly Func<BulkLabelImportDialogViewModel, bool> _confirmAction;
    private string _labelNamesText;

    public BulkLabelImportDialogViewModel(
        IReadOnlyList<string> existingLabels,
        Func<string[]?> loadFromFile,
        Func<BulkLabelImportDialogViewModel, bool> confirmAction,
        Action<ModalDialogViewModel> closeAction)
        : base(
            "Manage Label Set",
            "Paste one label per line or load them from a text file. Existing labels are preserved and duplicates are ignored.",
            closeAction)
    {
        OriginalLabels = existingLabels.ToArray();
        _labelNamesText = string.Join(Environment.NewLine, OriginalLabels);
        _loadFromFile = loadFromFile;
        _confirmAction = confirmAction;
        LoadFromFileCommand = new RelayCommand(LoadFromFile);
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
    }

    public IReadOnlyList<string> OriginalLabels { get; }

    public RelayCommand LoadFromFileCommand { get; }

    public RelayCommand ConfirmCommand { get; }

    public string LabelNamesText
    {
        get => _labelNamesText;
        set
        {
            if (SetProperty(ref _labelNamesText, value))
            {
                RaisePropertyChanged(nameof(ParsedLabelsPreview));
                RaisePropertyChanged(nameof(HasChanges));
                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<string> ParsedLabelsPreview => ParseEntries(LabelNamesText);

    public bool HasChanges => !ParsedLabelsPreview.SequenceEqual(OriginalLabels, StringComparer.OrdinalIgnoreCase);

    private void LoadFromFile()
    {
        var lines = _loadFromFile();
        if (lines is null || lines.Length == 0)
        {
            return;
        }

        LabelNamesText = string.Join(Environment.NewLine, lines);
    }

    private bool CanConfirm() => ParsedLabelsPreview.Count > 0 && HasChanges;

    private void Confirm()
    {
        if (_confirmAction(this))
        {
            RequestClose();
        }
    }

    public static IReadOnlyList<string> ParseEntries(string rawText)
    {
        return rawText
            .Split(['\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
