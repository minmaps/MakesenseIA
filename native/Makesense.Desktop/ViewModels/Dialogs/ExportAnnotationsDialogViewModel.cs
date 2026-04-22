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
        AvailableFormatOptions = availableFormats
            .Select(format => new AnnotationFormatOption(format, GetDisplayName(format)))
            .ToArray();
    }

    public IReadOnlyList<AnnotationFormat> AvailableFormats { get; }

    public IReadOnlyList<AnnotationFormatOption> AvailableFormatOptions { get; }

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
            AnnotationFormat.YoloImageTxt => ".zip",
            AnnotationFormat.Voc => ".zip",
            _ => ".json"
        };
    }

    private static string GetDisplayName(AnnotationFormat format)
    {
        return format switch
        {
            AnnotationFormat.Yolo => "YOLO package",
            AnnotationFormat.YoloImageTxt => "YOLO image .txt files",
            AnnotationFormat.Voc => "VOC XML package",
            AnnotationFormat.Csv => "CSV file",
            AnnotationFormat.Coco => "COCO JSON file",
            AnnotationFormat.Vgg => "VGG JSON file",
            AnnotationFormat.Json => "JSON file",
            _ => format.ToString()
        };
    }
}

public sealed record AnnotationFormatOption(AnnotationFormat Format, string DisplayName);
