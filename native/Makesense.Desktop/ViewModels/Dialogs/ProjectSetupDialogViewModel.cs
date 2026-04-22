using Makesense.Desktop.Commands;
using Makesense.Formats.Contracts;

namespace Makesense.Desktop.ViewModels.Dialogs;

public sealed class ProjectSetupDialogViewModel : ModalDialogViewModel
{
    private readonly Func<string[]?> _browseImages;
    private readonly Func<string[]?> _loadLabelsFromFile;
    private readonly Func<ProjectSetupDialogViewModel, Task<bool>> _confirmAction;
    private readonly Func<Task<bool>> _openExistingProjectAction;
    private string _projectName;
    private ProjectKind _selectedProjectKind;
    private IReadOnlyList<string> _imagePaths;
    private string _labelNamesText;
    private bool _isBusy;

    public ProjectSetupDialogViewModel(
        string projectName,
        ProjectKind selectedProjectKind,
        Func<string[]?> browseImages,
        Func<string[]?> loadLabelsFromFile,
        Func<ProjectSetupDialogViewModel, Task<bool>> confirmAction,
        Func<Task<bool>> openExistingProjectAction,
        Action<ModalDialogViewModel> closeAction,
        bool isCancelable = true)
        : base(
            "Create Project",
            "Choose the images and define at least one label before entering the workspace.",
            closeAction,
            isCancelable: isCancelable)
    {
        _projectName = string.IsNullOrWhiteSpace(projectName) ? "makesense-native" : projectName.Trim();
        _selectedProjectKind = selectedProjectKind;
        _imagePaths = Array.Empty<string>();
        _labelNamesText = string.Empty;
        _browseImages = browseImages;
        _loadLabelsFromFile = loadLabelsFromFile;
        _confirmAction = confirmAction;
        _openExistingProjectAction = openExistingProjectAction;
        BrowseImagesCommand = new RelayCommand(BrowseImages, () => !IsBusy);
        LoadLabelsFromFileCommand = new RelayCommand(LoadLabelsFromFile, () => !IsBusy);
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
        OpenExistingProjectCommand = new RelayCommand(OpenExistingProject, () => !IsBusy);
    }

    public IReadOnlyList<ProjectKind> AvailableProjectKinds { get; } = Enum.GetValues<ProjectKind>();

    public RelayCommand BrowseImagesCommand { get; }

    public RelayCommand LoadLabelsFromFileCommand { get; }

    public RelayCommand ConfirmCommand { get; }

    public RelayCommand OpenExistingProjectCommand { get; }

    public string ProjectName
    {
        get => _projectName;
        set
        {
            if (SetProperty(ref _projectName, value))
            {
                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ProjectKind SelectedProjectKind
    {
        get => _selectedProjectKind;
        set => SetProperty(ref _selectedProjectKind, value);
    }

    public IReadOnlyList<string> ImagePaths
    {
        get => _imagePaths;
        private set
        {
            if (SetProperty(ref _imagePaths, value))
            {
                RaisePropertyChanged(nameof(SelectedImagesSummary));
                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string LabelNamesText
    {
        get => _labelNamesText;
        set
        {
            if (SetProperty(ref _labelNamesText, value))
            {
                RaisePropertyChanged(nameof(ParsedLabelsPreview));
                RaisePropertyChanged(nameof(ParsedLabelsSummary));
                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public IReadOnlyList<string> ParsedLabelsPreview => BulkLabelImportDialogViewModel.ParseEntries(LabelNamesText);

    public string SelectedImagesSummary => ImagePaths.Count == 0
        ? "No image selected."
        : $"{ImagePaths.Count} image(s) selected.";

    public string ParsedLabelsSummary => ParsedLabelsPreview.Count == 0
        ? "No label ready."
        : $"{ParsedLabelsPreview.Count} label(s) ready.";

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                BrowseImagesCommand.RaiseCanExecuteChanged();
                LoadLabelsFromFileCommand.RaiseCanExecuteChanged();
                ConfirmCommand.RaiseCanExecuteChanged();
                OpenExistingProjectCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private void BrowseImages()
    {
        var paths = _browseImages();
        if (paths is null || paths.Length == 0)
        {
            return;
        }

        ImagePaths = paths;
    }

    private void LoadLabelsFromFile()
    {
        var lines = _loadLabelsFromFile();
        if (lines is null || lines.Length == 0)
        {
            return;
        }

        LabelNamesText = string.Join(Environment.NewLine, lines);
    }

    private bool CanConfirm()
    {
        return !IsBusy &&
            !string.IsNullOrWhiteSpace(ProjectName) &&
            ImagePaths.Count > 0 &&
            ParsedLabelsPreview.Count > 0;
    }

    private async void Confirm()
    {
        IsBusy = true;
        try
        {
            if (await _confirmAction(this))
            {
                RequestClose();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async void OpenExistingProject()
    {
        IsBusy = true;
        try
        {
            if (await _openExistingProjectAction())
            {
                RequestClose();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }
}
