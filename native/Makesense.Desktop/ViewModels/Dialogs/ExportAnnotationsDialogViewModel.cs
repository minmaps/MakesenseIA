using Makesense.Desktop.Commands;
using Makesense.Formats.Contracts;

namespace Makesense.Desktop.ViewModels.Dialogs;

public sealed class ExportAnnotationsDialogViewModel : ModalDialogViewModel
{
    private readonly Func<AnnotationFormat, string?> _browseDestination;
    private readonly Func<ExportAnnotationsDialogViewModel, bool> _confirmAction;
    private AnnotationFormat _selectedFormat;
    private string _destinationPath;

    public ExportAnnotationsDialogViewModel(
        IReadOnlyList<AnnotationFormat> availableFormats,
        AnnotationFormat selectedFormat,
        string destinationPath,
        Func<AnnotationFormat, string?> browseDestination,
        Func<ExportAnnotationsDialogViewModel, bool> confirmAction,
        Action<ModalDialogViewModel> closeAction)
        : base(
            "Export Annotations",
            "Choose the target format and output path for the current project annotations.",
            closeAction)
    {
        AvailableFormats = availableFormats;
        _selectedFormat = selectedFormat;
        _destinationPath = destinationPath;
        _browseDestination = browseDestination;
        _confirmAction = confirmAction;
        BrowseDestinationCommand = new RelayCommand(BrowseDestination);
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
    }

    public IReadOnlyList<AnnotationFormat> AvailableFormats { get; }

    public RelayCommand BrowseDestinationCommand { get; }

    public RelayCommand ConfirmCommand { get; }

    public AnnotationFormat SelectedFormat
    {
        get => _selectedFormat;
        set
        {
            if (SetProperty(ref _selectedFormat, value))
            {
                RaisePropertyChanged(nameof(DefaultExtensionHint));
                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string DestinationPath
    {
        get => _destinationPath;
        set
        {
            if (SetProperty(ref _destinationPath, value))
            {
                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string DefaultExtensionHint => $"Suggested extension: {GetDefaultExtension(SelectedFormat)}";

    private void BrowseDestination()
    {
        var selectedPath = _browseDestination(SelectedFormat);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            DestinationPath = selectedPath;
        }
    }

    private bool CanConfirm() => !string.IsNullOrWhiteSpace(DestinationPath);

    private void Confirm()
    {
        if (_confirmAction(this))
        {
            RequestClose();
        }
    }

    private static string GetDefaultExtension(AnnotationFormat format)
    {
        return format switch
        {
            AnnotationFormat.Csv => ".csv",
            AnnotationFormat.Coco => ".json",
            AnnotationFormat.Vgg => ".json",
            AnnotationFormat.Json => ".json",
            AnnotationFormat.Yolo => ".zip",
            AnnotationFormat.Voc => ".zip",
            _ => ".json"
        };
    }
}
