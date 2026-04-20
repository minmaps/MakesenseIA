using Makesense.Desktop.Commands;
using Makesense.Formats.Contracts;

namespace Makesense.Desktop.ViewModels.Dialogs;

public sealed class ImportAnnotationsDialogViewModel : ModalDialogViewModel
{
    private readonly Func<AnnotationFormat, string[]?> _browseSources;
    private readonly Func<ImportAnnotationsDialogViewModel, bool> _confirmAction;
    private AnnotationFormat _selectedFormat;
    private string[] _selectedPaths;

    public ImportAnnotationsDialogViewModel(
        IReadOnlyList<AnnotationFormat> availableFormats,
        AnnotationFormat selectedFormat,
        Func<AnnotationFormat, string[]?> browseSources,
        Func<ImportAnnotationsDialogViewModel, bool> confirmAction,
        Action<ModalDialogViewModel> closeAction)
        : base(
            "Import Annotations",
            "Choose an annotation format and input files. Import replaces the current geometry of the imported kinds on matching images.",
            closeAction)
    {
        AvailableFormats = availableFormats;
        _selectedFormat = selectedFormat;
        _selectedPaths = Array.Empty<string>();
        _browseSources = browseSources;
        _confirmAction = confirmAction;
        BrowseSourcesCommand = new RelayCommand(BrowseSources);
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
    }

    public IReadOnlyList<AnnotationFormat> AvailableFormats { get; }

    public RelayCommand BrowseSourcesCommand { get; }

    public RelayCommand ConfirmCommand { get; }

    public AnnotationFormat SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (SetProperty(ref _selectedFormat, value))
            {
                _selectedPaths = Array.Empty<string>();
                RaisePropertyChanged(nameof(SelectedPaths));
                RaisePropertyChanged(nameof(AllowsMultipleFiles));
                RaisePropertyChanged(nameof(SelectedPathsSummary));
                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<string> SelectedPaths => _selectedPaths;

    public bool AllowsMultipleFiles => SelectedFormat is AnnotationFormat.Yolo or AnnotationFormat.Voc;

    public string SelectedPathsSummary => _selectedPaths.Length switch
    {
        0 => "No source selected.",
        1 => _selectedPaths[0],
        _ => $"{_selectedPaths.Length} files selected."
    };

    private void BrowseSources()
    {
        var selectedPaths = _browseSources(SelectedFormat);
        if (selectedPaths is null || selectedPaths.Length == 0)
        {
            return;
        }

        _selectedPaths = selectedPaths;
        RaisePropertyChanged(nameof(SelectedPaths));
        RaisePropertyChanged(nameof(SelectedPathsSummary));
        ConfirmCommand.RaiseCanExecuteChanged();
    }

    private bool CanConfirm() => _selectedPaths.Length > 0;

    private void Confirm()
    {
        if (_confirmAction(this))
        {
            RequestClose();
        }
    }
}
