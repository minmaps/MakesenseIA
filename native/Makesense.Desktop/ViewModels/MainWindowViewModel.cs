using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using Makesense.Desktop.Commands;
using Makesense.Desktop.Interop;
using Makesense.Desktop.Services;
using Makesense.Desktop.ViewModels.Dialogs;
using Makesense.Formats.Contracts;
using Makesense.Formats.Export;
using Makesense.Formats.Import;
using Makesense.Formats.Project;
using Makesense.Formats.Serialization;

namespace Makesense.Desktop.ViewModels;

public sealed class MainWindowViewModel : ViewModelBase, IDisposable
{
    private readonly PerformanceConfigService _performanceConfigService;
    private readonly ImageMetadataService _imageMetadataService;
    private readonly AnnotationImportService _annotationImportService;
    private readonly AnnotationExportService _annotationExportService;

    private ProjectState _projectState;
    private string _statusMessage;
    private string _projectPath;
    private string _activeModelPath;
    private string _selectedInferenceTask;
    private string _newLabelName;
    private string _imageSearchQuery;
    private string _activeInferenceBackend;
    private string _activeInferenceProvider;
    private string _activeInferenceRuntime;
    private ImageRecord? _selectedImage;
    private LabelClass? _selectedLabel;
    private AnnotationItemViewModel? _selectedAnnotation;
    private AnnotationTool _selectedTool;
    private AnnotationFormat _selectedImportFormat;
    private AnnotationFormat _selectedExportFormat;
    private ImageFilterMode _selectedImageFilter;
    private double _viewZoom = 1.0;
    private double _panOffsetX;
    private double _panOffsetY;
    private LabelClass? _selectedAnnotationLabel;
    private string _selectedAnnotationSuggestedLabel;
    private bool _selectedAnnotationIsVisible = true;
    private string _rectX;
    private string _rectY;
    private string _rectWidth;
    private string _rectHeight;
    private string _pointX;
    private string _pointY;
    private string _lineStartX;
    private string _lineStartY;
    private string _lineEndX;
    private string _lineEndY;
    private string _polygonPointsText;
    private ModalDialogViewModel? _activeDialog;
    private readonly DispatcherTimer _notificationTimer;
    private readonly Dispatcher _uiDispatcher;
    private int _inferenceBatchCompletedImages;
    private int _inferenceBatchTotalImages;
    private bool _isInferenceBatchRunning;

    public MainWindowViewModel(
        PerformanceConfigService performanceConfigService,
        NativeEngineSession engineSession,
        PerformanceConfig initialConfig)
    {
        _performanceConfigService = performanceConfigService;
        _imageMetadataService = new ImageMetadataService();
        _annotationImportService = new AnnotationImportService();
        _annotationExportService = new AnnotationExportService();
        EngineSession = engineSession;
        Performance = new PerformanceConfigViewModel();
        Performance.Load(initialConfig);

        _statusMessage = "Native session ready.";
        _projectPath = "No project loaded.";
        _activeModelPath = string.Empty;
        _selectedInferenceTask = "rect-detection";
        _newLabelName = string.Empty;
        _imageSearchQuery = string.Empty;
        _activeInferenceBackend = "Not run";
        _activeInferenceProvider = "N/A";
        _activeInferenceRuntime = "Native inference idle.";
        _selectedAnnotationSuggestedLabel = string.Empty;
        _rectX = string.Empty;
        _rectY = string.Empty;
        _rectWidth = string.Empty;
        _rectHeight = string.Empty;
        _pointX = string.Empty;
        _pointY = string.Empty;
        _lineStartX = string.Empty;
        _lineStartY = string.Empty;
        _lineEndX = string.Empty;
        _lineEndY = string.Empty;
        _polygonPointsText = string.Empty;
        _selectedTool = AnnotationTool.Rect;
        _selectedImportFormat = AnnotationFormat.Coco;
        _selectedExportFormat = AnnotationFormat.Yolo;
        _selectedImageFilter = ImageFilterMode.All;
        _uiDispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _notificationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _notificationTimer.Tick += OnNotificationTimerTick;

        InferenceTasks =
        [
            "rect-detection",
            "pose-estimation"
        ];

        AvailableTools =
        [
            AnnotationTool.Rect,
            AnnotationTool.Point,
            AnnotationTool.Line,
            AnnotationTool.Polygon,
            AnnotationTool.ImageRecognition
        ];

        OpenImagesCommand = new RelayCommand(OpenImages);
        AddImagesCommand = new RelayCommand(AddImages, () => Images.Count > 0);
        OpenProjectCommand = new RelayCommand(OpenProject);
        ExportManifestCommand = new RelayCommand(ExportManifest);
        ImportAnnotationsCommand = new RelayCommand(OpenImportAnnotationsDialog, () => Images.Count > 0);
        ExportAnnotationsCommand = new RelayCommand(OpenExportAnnotationsDialog, () => Images.Count > 0);
        ApplyPerformanceLimitsCommand = new RelayCommand(ApplyPerformanceLimits);
        RunInferenceCommand = new RelayCommand(OpenInferenceDialog);
        AddLabelCommand = new RelayCommand(AddLabel);
        OpenLabelManagerCommand = new RelayCommand(OpenLabelManagerDialog);
        RemoveLabelCommand = new RelayCommand(RemoveLabel, () => SelectedLabel is not null);
        AddAnnotationCommand = new RelayCommand(AddAnnotation, () => SelectedImage is not null);
        DeleteAnnotationCommand = new RelayCommand(DeleteAnnotation, () => SelectedAnnotation is not null && SelectedImage is not null);
        PreviousImageCommand = new RelayCommand(SelectPreviousImage, CanSelectPreviousImage);
        NextImageCommand = new RelayCommand(SelectNextImage, CanSelectNextImage);
        ApplySelectedAnnotationPropertiesCommand = new RelayCommand(ApplySelectedAnnotationProperties, () => SelectedAnnotation is not null);
        AcceptSuggestedLabelCommand = new RelayCommand(AcceptSuggestedLabel, () => SelectedAnnotation is not null && !string.IsNullOrWhiteSpace(SelectedAnnotationSuggestedLabel));
        RejectSuggestedLabelCommand = new RelayCommand(RejectSuggestedLabel, () => SelectedAnnotation is not null && !string.IsNullOrWhiteSpace(SelectedAnnotationSuggestedLabel));
        ToggleSelectedAnnotationVisibilityCommand = new RelayCommand(ToggleSelectedAnnotationVisibility, () => SelectedAnnotation is not null);
        ClearSelectedAnnotationLabelCommand = new RelayCommand(ClearSelectedAnnotationLabel, () => SelectedAnnotation is not null && SelectedAnnotationLabel is not null);
        AcceptAllSuggestionsCommand = new RelayCommand(AcceptAllSuggestions, () => SuggestedAnnotationCount > 0);
        RejectAllSuggestionsCommand = new RelayCommand(RejectAllSuggestions, () => SuggestedAnnotationCount > 0);
        AcceptAllDatasetSuggestionsCommand = new RelayCommand(OpenAcceptAllDatasetSuggestionsDialog, () => SuggestionImageCount > 0);
        RejectAllDatasetSuggestionsCommand = new RelayCommand(OpenRejectAllDatasetSuggestionsDialog, () => SuggestionImageCount > 0);
        NextPendingReviewImageCommand = new RelayCommand(SelectNextPendingReviewImage, () => PendingReviewImageCount > 0);
        NextSuggestionImageCommand = new RelayCommand(SelectNextSuggestionImage, () => SuggestionImageCount > 0);
        ClearNotificationsCommand = new RelayCommand(ClearNotifications, () => Notifications.Count > 0);

        _projectState = new ProjectState
        {
            Name = "makesense-native",
            ProjectKind = ProjectKind.ObjectDetection,
            ProjectPath = ProjectPath,
            PerformanceConfig = initialConfig.Normalize()
        };

        FilteredImagesView = CollectionViewSource.GetDefaultView(Images);
        FilteredImagesView.Filter = FilterImage;
        ApplyToolFormatDefaults();
        EngineSession.StatusChanged += OnSessionStatusChanged;
        _notificationTimer.Start();
        LoadSelectedAnnotationEditor();
    }

    public ObservableCollection<ImageRecord> Images { get; } = [];

    public ObservableCollection<LabelClass> Labels { get; } = [];

    public ObservableCollection<AnnotationItemViewModel> Annotations { get; } = [];

    public ObservableCollection<NotificationEntryViewModel> Notifications { get; } = [];

    public IReadOnlyList<string> InferenceTasks { get; }

    public IReadOnlyList<AnnotationTool> AvailableTools { get; }

    public IReadOnlyList<ImageFilterMode> AvailableImageFilters { get; } =
    [
        ImageFilterMode.All,
        ImageFilterMode.CurrentToolPending,
        ImageFilterMode.CurrentToolAnnotated,
        ImageFilterMode.WithSuggestions
    ];

    public IReadOnlyList<AnnotationFormat> AvailableImportFormats => FormatSupport.GetImportFormats(MapTool(SelectedTool));

    public IReadOnlyList<AnnotationFormat> AvailableExportFormats => FormatSupport.GetExportFormats(MapTool(SelectedTool));

    public RelayCommand OpenImagesCommand { get; }

    public RelayCommand AddImagesCommand { get; }

    public RelayCommand OpenProjectCommand { get; }

    public RelayCommand ExportManifestCommand { get; }

    public RelayCommand ImportAnnotationsCommand { get; }

    public RelayCommand ExportAnnotationsCommand { get; }

    public RelayCommand ApplyPerformanceLimitsCommand { get; }

    public RelayCommand RunInferenceCommand { get; }

    public RelayCommand AddLabelCommand { get; }

    public RelayCommand OpenLabelManagerCommand { get; }

    public RelayCommand RemoveLabelCommand { get; }

    public RelayCommand AddAnnotationCommand { get; }

    public RelayCommand DeleteAnnotationCommand { get; }

    public RelayCommand PreviousImageCommand { get; }

    public RelayCommand NextImageCommand { get; }

    public RelayCommand ApplySelectedAnnotationPropertiesCommand { get; }

    public RelayCommand AcceptSuggestedLabelCommand { get; }

    public RelayCommand RejectSuggestedLabelCommand { get; }

    public RelayCommand ToggleSelectedAnnotationVisibilityCommand { get; }

    public RelayCommand ClearSelectedAnnotationLabelCommand { get; }

    public RelayCommand AcceptAllSuggestionsCommand { get; }

    public RelayCommand RejectAllSuggestionsCommand { get; }

    public RelayCommand AcceptAllDatasetSuggestionsCommand { get; }

    public RelayCommand RejectAllDatasetSuggestionsCommand { get; }

    public RelayCommand NextPendingReviewImageCommand { get; }

    public RelayCommand NextSuggestionImageCommand { get; }

    public RelayCommand ClearNotificationsCommand { get; }

    public NativeEngineSession EngineSession { get; }

    public PerformanceConfigViewModel Performance { get; }

    public ICollectionView FilteredImagesView { get; }

    public string RuntimeSummary => "WPF + HwndHost + C++20 Core + D3D11/Direct2D";

    public string RenderHostHint => "Native rendering for viewport, desktop store for project parity and formats.";

    public string ActiveInferenceBackend
    {
        get => _activeInferenceBackend;
        private set => SetProperty(ref _activeInferenceBackend, value);
    }

    public string ActiveInferenceProvider
    {
        get => _activeInferenceProvider;
        private set => SetProperty(ref _activeInferenceProvider, value);
    }

    public string ActiveInferenceRuntime
    {
        get => _activeInferenceRuntime;
        private set => SetProperty(ref _activeInferenceRuntime, value);
    }

    public double InferenceBatchProgressPercent => _inferenceBatchTotalImages == 0
        ? 0
        : 100.0 * _inferenceBatchCompletedImages / _inferenceBatchTotalImages;

    public string InferenceBatchProgressLabel => _inferenceBatchTotalImages == 0
        ? "Idle"
        : $"{_inferenceBatchCompletedImages}/{_inferenceBatchTotalImages}";

    public bool IsInferenceBatchRunning
    {
        get => _isInferenceBatchRunning;
        private set => SetProperty(ref _isInferenceBatchRunning, value);
    }

    public ModalDialogViewModel? ActiveDialog
    {
        get => _activeDialog;
        private set
        {
            if (SetProperty(ref _activeDialog, value))
            {
                RaisePropertyChanged(nameof(IsDialogOpen));
            }
        }
    }

    public bool IsDialogOpen => ActiveDialog is not null;

    public string ProjectName
    {
        get => _projectState.Name;
        set
        {
            var normalized = value.Trim();
            if (string.IsNullOrWhiteSpace(normalized) || string.Equals(_projectState.Name, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _projectState = _projectState with { Name = normalized };
            RaisePropertyChanged();
        }
    }

    public IReadOnlyList<AnnotationRecord> CurrentAnnotations => SelectedImage?.Annotations ?? Array.Empty<AnnotationRecord>();

    public string? SelectedAnnotationId => SelectedAnnotation?.Record.Id;

    public IReadOnlyList<LabelClass> AnnotationLabelOptions => Labels;

    public int SuggestedAnnotationCount => CurrentAnnotations.Count(annotation => !string.IsNullOrWhiteSpace(annotation.SuggestedLabel));

    public double ProjectProgressPercent => Images.Count == 0
        ? 0
        : 100.0 * Images.Count(ImageHasCurrentToolAnnotations) / Images.Count;

    public int FilteredImageCount => FilteredImagesView.Cast<object>().Count();

    public int SuggestionImageCount => Images.Count(ImageHasSuggestions);

    public int PendingReviewImageCount => Images.Count(image => ImageNeedsReview(image));

    public bool HasImagesLoaded => Images.Count > 0;

    public bool HasFilteredImages => FilteredImageCount > 0;

    public string EmptyStateMessage => HasImagesLoaded
        ? "No image matches the current filter."
        : "No images loaded.";

    public string EmptyStateHint => HasImagesLoaded
        ? "Change the filter or search query, or add more images."
        : "Open a dataset or drag in images to start annotating.";

    public double ViewZoom
    {
        get => _viewZoom;
        set => SetProperty(ref _viewZoom, value);
    }

    public double PanOffsetX
    {
        get => _panOffsetX;
        set => SetProperty(ref _panOffsetX, value);
    }

    public double PanOffsetY
    {
        get => _panOffsetY;
        set => SetProperty(ref _panOffsetY, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                PushNotification(value);
            }
        }
    }

    public string ProjectPath
    {
        get => _projectPath;
        private set => SetProperty(ref _projectPath, value);
    }

    public string ActiveModelPath
    {
        get => _activeModelPath;
        set => SetProperty(ref _activeModelPath, value);
    }

    public string SelectedInferenceTask
    {
        get => _selectedInferenceTask;
        set => SetProperty(ref _selectedInferenceTask, value);
    }

    public string NewLabelName
    {
        get => _newLabelName;
        set => SetProperty(ref _newLabelName, value);
    }

    public string ImageSearchQuery
    {
        get => _imageSearchQuery;
        set
        {
            if (SetProperty(ref _imageSearchQuery, value))
            {
                RefreshImageBrowser();
            }
        }
    }

    public ImageFilterMode SelectedImageFilter
    {
        get => _selectedImageFilter;
        set
        {
            if (SetProperty(ref _selectedImageFilter, value))
            {
                RefreshImageBrowser();
            }
        }
    }

    public AnnotationTool SelectedTool
    {
        get => _selectedTool;
        set
        {
            if (!SetProperty(ref _selectedTool, value))
            {
                return;
            }

            ApplyToolFormatDefaults();
            RaisePropertyChanged(nameof(AvailableImportFormats));
            RaisePropertyChanged(nameof(AvailableExportFormats));
            RaisePropertyChanged(nameof(ProjectProgressPercent));
            RefreshImageBrowser();
        }
    }

    public AnnotationFormat SelectedImportFormat
    {
        get => _selectedImportFormat;
        set => SetProperty(ref _selectedImportFormat, value);
    }

    public AnnotationFormat SelectedExportFormat
    {
        get => _selectedExportFormat;
        set => SetProperty(ref _selectedExportFormat, value);
    }

    public ImageRecord? SelectedImage
    {
        get => _selectedImage;
        set => ApplySelectedImage(value, updateProjectState: true, updateNativeEngine: true);
    }

    public LabelClass? SelectedLabel
    {
        get => _selectedLabel;
        set
        {
            if (SetProperty(ref _selectedLabel, value))
            {
                RemoveLabelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public LabelClass? SelectedAnnotationLabel
    {
        get => _selectedAnnotationLabel;
        set
        {
            if (SetProperty(ref _selectedAnnotationLabel, value))
            {
                ClearSelectedAnnotationLabelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedAnnotationSuggestedLabel
    {
        get => _selectedAnnotationSuggestedLabel;
        set
        {
            if (SetProperty(ref _selectedAnnotationSuggestedLabel, value))
            {
                AcceptSuggestedLabelCommand.RaiseCanExecuteChanged();
                RejectSuggestedLabelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool SelectedAnnotationIsVisible
    {
        get => _selectedAnnotationIsVisible;
        set => SetProperty(ref _selectedAnnotationIsVisible, value);
    }

    public bool HasSelectedAnnotation => SelectedAnnotation is not null;

    public bool IsRectAnnotationSelected => SelectedAnnotation?.Record.Kind == AnnotationKind.Rect;

    public bool IsPointAnnotationSelected => SelectedAnnotation?.Record.Kind == AnnotationKind.Point;

    public bool IsLineAnnotationSelected => SelectedAnnotation?.Record.Kind == AnnotationKind.Line;

    public bool IsPolygonAnnotationSelected => SelectedAnnotation?.Record.Kind == AnnotationKind.Polygon;

    public bool IsImageRecognitionAnnotationSelected => SelectedAnnotation?.Record.Kind == AnnotationKind.ImageRecognition;

    public string RectX
    {
        get => _rectX;
        set => SetProperty(ref _rectX, value);
    }

    public string RectY
    {
        get => _rectY;
        set => SetProperty(ref _rectY, value);
    }

    public string RectWidth
    {
        get => _rectWidth;
        set => SetProperty(ref _rectWidth, value);
    }

    public string RectHeight
    {
        get => _rectHeight;
        set => SetProperty(ref _rectHeight, value);
    }

    public string PointX
    {
        get => _pointX;
        set => SetProperty(ref _pointX, value);
    }

    public string PointY
    {
        get => _pointY;
        set => SetProperty(ref _pointY, value);
    }

    public string LineStartX
    {
        get => _lineStartX;
        set => SetProperty(ref _lineStartX, value);
    }

    public string LineStartY
    {
        get => _lineStartY;
        set => SetProperty(ref _lineStartY, value);
    }

    public string LineEndX
    {
        get => _lineEndX;
        set => SetProperty(ref _lineEndX, value);
    }

    public string LineEndY
    {
        get => _lineEndY;
        set => SetProperty(ref _lineEndY, value);
    }

    public string PolygonPointsText
    {
        get => _polygonPointsText;
        set => SetProperty(ref _polygonPointsText, value);
    }

    public AnnotationItemViewModel? SelectedAnnotation
    {
        get => _selectedAnnotation;
        set
        {
            if (SetProperty(ref _selectedAnnotation, value))
            {
                LoadSelectedAnnotationEditor();
                RaisePropertyChanged(nameof(SelectedAnnotationId));
                RaisePropertyChanged(nameof(HasSelectedAnnotation));
                RaisePropertyChanged(nameof(IsRectAnnotationSelected));
                RaisePropertyChanged(nameof(IsPointAnnotationSelected));
                RaisePropertyChanged(nameof(IsLineAnnotationSelected));
                RaisePropertyChanged(nameof(IsPolygonAnnotationSelected));
                RaisePropertyChanged(nameof(IsImageRecognitionAnnotationSelected));
                DeleteAnnotationCommand.RaiseCanExecuteChanged();
                ApplySelectedAnnotationPropertiesCommand.RaiseCanExecuteChanged();
                ToggleSelectedAnnotationVisibilityCommand.RaiseCanExecuteChanged();
                ClearSelectedAnnotationLabelCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedImageSummary => SelectedImage is null
        ? "No active image."
        : $"{SelectedImage.FileName} | {SelectedImage.Annotations.Count} annotations | {SelectedImage.Annotations.Count(annotation => !string.IsNullOrWhiteSpace(annotation.SuggestedLabel))} suggestions | {SelectedImage.FileSizeBytes / 1024.0:0.0} KB";

    public void Dispose()
    {
        EngineSession.StatusChanged -= OnSessionStatusChanged;
        _notificationTimer.Tick -= OnNotificationTimerTick;
        _notificationTimer.Stop();
        EngineSession.Dispose();
    }

    private void ApplyPerformanceLimits()
    {
        var config = Performance.ToModel();
        Performance.Load(config);
        _performanceConfigService.Save(config);
        _projectState = _projectState with { PerformanceConfig = config };
        EngineSession.SetPerformanceLimits(config);
        StatusMessage = "Performance limits saved and pushed to the native core.";
    }

    private async void OpenProject()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Makesense Manifest (*.json)|*.json|All Files (*.*)|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var state = await ProjectStateSerializer.LoadAsync(dialog.FileName);
        if (state is null)
        {
            StatusMessage = "Unable to read project manifest.";
            return;
        }

        ReplaceState(state with { PerformanceConfig = state.PerformanceConfig.Normalize() });
        _performanceConfigService.Save(_projectState.PerformanceConfig);

        ProjectPath = dialog.FileName;
        EngineSession.OpenProject(dialog.FileName);
        EngineSession.OpenImages(_projectState.Images.Select(image => image.Path));

        SelectedImage = _projectState.Images.FirstOrDefault(image => image.Id == _projectState.ActiveImageId) ?? _projectState.Images.FirstOrDefault();
        SelectedLabel = _projectState.Labels.FirstOrDefault();
        ActiveModelPath = _projectState.ActiveModel?.Path ?? string.Empty;
        SelectedInferenceTask = _projectState.ActiveModel?.Task ?? SelectedInferenceTask;
        ResetInferenceBatchState();
        StatusMessage = $"Loaded manifest with {Images.Count} images and {Labels.Count} labels.";
    }

    private void OpenImages()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.webp",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var images = dialog.FileNames.Select(_imageMetadataService.CreateRecord).ToArray();
        ReplaceState(new ProjectState
        {
            Name = "makesense-native",
            ProjectKind = ProjectKind.ObjectDetection,
            ProjectPath = "Loose image session",
            PerformanceConfig = Performance.ToModel(),
            Images = images,
            Labels = _projectState.Labels
        });

        ResetInferenceBatchState();
        ProjectPath = "Loose image session";
        EngineSession.OpenImages(dialog.FileNames);
        SelectedImage = Images.FirstOrDefault();
        StatusMessage = $"{Images.Count} images loaded into the desktop session.";
    }

    private void AddImages()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff;*.webp",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var knownPaths = _projectState.Images
            .Select(image => image.Path)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newImagePaths = dialog.FileNames
            .Where(path => !knownPaths.Contains(path))
            .ToArray();

        if (newImagePaths.Length == 0)
        {
            StatusMessage = "No new images were added.";
            return;
        }

        var appendedImages = _projectState.Images
            .Concat(newImagePaths.Select(_imageMetadataService.CreateRecord))
            .ToArray();

        ReplaceState(_projectState.ReplaceImages(appendedImages));
        ResetInferenceBatchState();
        EngineSession.OpenImages(appendedImages.Select(image => image.Path));
        StatusMessage = $"{newImagePaths.Length} image(s) added to the current session.";
    }

    private async void ExportManifest()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Makesense Manifest (*.json)|*.json",
            FileName = "makesense-native-project.json"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var state = _projectState with
        {
            ProjectPath = ProjectPath,
            ActiveImageId = SelectedImage?.Id,
            ActiveModel = string.IsNullOrWhiteSpace(ActiveModelPath)
                ? null
                : new InferenceModelDescriptor
                {
                    Name = Path.GetFileNameWithoutExtension(ActiveModelPath),
                    Path = ActiveModelPath,
                    Task = SelectedInferenceTask,
                    Backend = ActiveInferenceProvider
                }
        };

        await ProjectStateSerializer.SaveAsync(state, dialog.FileName);
        StatusMessage = $"Manifest exported to {dialog.FileName}.";
    }

    private void OpenImportAnnotationsDialog()
    {
        ShowDialog(new ImportAnnotationsDialogViewModel(
            AvailableImportFormats,
            SelectedImportFormat,
            BrowseImportPaths,
            ConfirmImportAnnotations,
            CloseDialog));
    }

    private void OpenExportAnnotationsDialog()
    {
        ShowDialog(new ExportAnnotationsDialogViewModel(
            AvailableExportFormats,
            SelectedExportFormat,
            BuildDefaultExportPath(SelectedExportFormat),
            BrowseExportDestination,
            ConfirmExportAnnotations,
            CloseDialog));
    }

    private void OpenInferenceDialog()
    {
        ShowDialog(new InferenceSetupDialogViewModel(
            InferenceTasks,
            SelectedInferenceTask,
            ActiveModelPath,
            BrowseModelPath,
            ConfirmRunInference,
            CloseDialog));
    }

    private void AddLabel()
    {
        var normalizedName = NewLabelName.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return;
        }

        if (_projectState.Labels.Any(label => string.Equals(label.Name, normalizedName, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = "Label already exists.";
            return;
        }

        var nextLabel = new LabelClass
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = normalizedName
        };

        ReplaceState(_projectState.ReplaceLabels(_projectState.Labels.Concat([nextLabel]).ToArray()));
        SelectedLabel = nextLabel;
        if (SelectedAnnotationSuggestedLabel.Equals(nextLabel.Name, StringComparison.OrdinalIgnoreCase))
        {
            SelectedAnnotationLabel = Labels.FirstOrDefault(label => label.Id == nextLabel.Id);
        }
        NewLabelName = string.Empty;
        StatusMessage = $"Label '{nextLabel.Name}' added.";
    }

    private void OpenLabelManagerDialog()
    {
        ShowDialog(new BulkLabelImportDialogViewModel(
            Labels.Select(label => label.Name).ToArray(),
            LoadLabelNamesFromFile,
            ConfirmBulkLabelImport,
            CloseDialog));
    }

    private void RemoveLabel()
    {
        if (SelectedLabel is null)
        {
            return;
        }

        var labelId = SelectedLabel.Id;
        var selectedImageId = SelectedImage?.Id;
        var images = _projectState.Images
            .Select(image => image with
            {
                Annotations = image.Annotations
                    .Select(annotation => annotation.LabelId == labelId ? annotation with { LabelId = null } : annotation)
                    .ToArray()
            })
            .ToArray();

        ReplaceState(_projectState
            .ReplaceLabels(_projectState.Labels.Where(label => label.Id != labelId).ToArray())
            .ReplaceImages(images));
        SelectedImage = _projectState.Images.FirstOrDefault(image => image.Id == selectedImageId) ?? _projectState.Images.FirstOrDefault();
        SelectedLabel = Labels.FirstOrDefault();
        StatusMessage = "Label removed. Existing annotations were kept and unlabeled.";
    }

    private void AddAnnotation()
    {
        if (SelectedImage is null)
        {
            return;
        }

        var annotation = CreateDefaultAnnotation(SelectedTool, SelectedImage.PixelSize, SelectedLabel?.Id);
        var updatedImage = SelectedImage with { Annotations = SelectedImage.Annotations.Concat([annotation]).ToArray() };
        ReplaceImage(updatedImage);
        SelectedImage = updatedImage;
        SelectedAnnotation = Annotations.FirstOrDefault(item => item.Record.Id == annotation.Id);
        StatusMessage = $"Added {SelectedTool} annotation.";
    }

    private void DeleteAnnotation()
    {
        if (SelectedImage is null || SelectedAnnotation is null)
        {
            return;
        }

        var updatedImage = SelectedImage with
        {
            Annotations = SelectedImage.Annotations
                .Where(annotation => annotation.Id != SelectedAnnotation.Record.Id)
                .ToArray()
        };

        ReplaceImage(updatedImage);
        SelectedImage = updatedImage;
        SelectedAnnotation = null;
        StatusMessage = "Annotation deleted.";
    }

    private void ReplaceImage(ImageRecord image)
    {
        ReplaceState(_projectState.ReplaceImage(image));
    }

    public void ForwardViewportPointer(Point viewportPoint)
    {
        EngineSession.HandleMouseMove((float)viewportPoint.X, (float)viewportPoint.Y);
    }

    public void UpdateViewportTransform(double zoom, double panOffsetX, double panOffsetY)
    {
        ViewZoom = zoom;
        PanOffsetX = panOffsetX;
        PanOffsetY = panOffsetY;
        EngineSession.SetViewTransform(zoom, panOffsetX, panOffsetY);
    }

    public void ZoomViewport(double factor)
    {
        UpdateViewportTransform(Math.Clamp(ViewZoom * factor, 0.1, 12.0), PanOffsetX, PanOffsetY);
    }

    public void ResetViewport()
    {
        UpdateViewportTransform(1.0, 0.0, 0.0);
    }

    public void SelectAnnotationById(string? annotationId)
    {
        SelectedAnnotation = annotationId is null
            ? null
            : Annotations.FirstOrDefault(annotation => annotation.Record.Id == annotationId);
    }

    public void UpsertAnnotationFromViewport(AnnotationRecord rawAnnotation, bool isUpdate)
    {
        var annotation = rawAnnotation with
        {
            LabelId = isUpdate
                ? rawAnnotation.LabelId
                : SelectedLabel?.Id ?? rawAnnotation.LabelId
        };

        if (SelectedImage is null)
        {
            return;
        }

        ImageRecord updatedImage;
        if (isUpdate)
        {
            updatedImage = SelectedImage with
            {
                Annotations = SelectedImage.Annotations
                    .Select(existing => existing.Id == annotation.Id ? annotation : existing)
                    .ToArray()
            };
        }
        else
        {
            updatedImage = SelectedImage with { Annotations = SelectedImage.Annotations.Concat([annotation]).ToArray() };
        }

        ReplaceImage(updatedImage);
        SelectedImage = updatedImage;
        SelectAnnotationById(annotation.Id);
        StatusMessage = isUpdate ? "Annotation updated from viewport." : "Annotation created from viewport.";
    }

    public void DeleteSelectedAnnotationFromViewport()
    {
        DeleteAnnotation();
    }

    public void SelectPreviousImage()
    {
        SelectImageByOffset(-1);
    }

    public void SelectNextImage()
    {
        SelectImageByOffset(1);
    }

    public void SelectNextPendingReviewImage()
    {
        SelectNextImageMatching(ImageNeedsReview, "No more pending review images in the current dataset.");
    }

    public void SelectNextSuggestionImage()
    {
        SelectNextImageMatching(ImageHasSuggestions, "No more images with AI suggestions in the current dataset.");
    }

    private void OpenAcceptAllDatasetSuggestionsDialog()
    {
        ShowDialog(new ConfirmActionDialogViewModel(
            "Accept Dataset Suggestions",
            "This applies every suggested label across the dataset and clears the suggestion state on each matching annotation.",
            "Accept Dataset Suggestions",
            ConfirmAcceptAllDatasetSuggestions,
            CloseDialog));
    }

    private void OpenRejectAllDatasetSuggestionsDialog()
    {
        ShowDialog(new ConfirmActionDialogViewModel(
            "Reject Dataset Suggestions",
            "This clears every pending AI suggestion across the dataset. Existing assigned labels are kept.",
            "Reject Dataset Suggestions",
            ConfirmRejectAllDatasetSuggestions,
            CloseDialog));
    }

    public void NudgeSelectedAnnotation(double dx, double dy)
    {
        if (SelectedImage is null || SelectedAnnotation is null)
        {
            return;
        }

        var annotation = SelectedAnnotation.Record.Kind switch
        {
            AnnotationKind.Rect when SelectedAnnotation.Record.Rect is not null => SelectedAnnotation.Record with
            {
                Rect = SelectedAnnotation.Record.Rect.Value with
                {
                    X = SelectedAnnotation.Record.Rect.Value.X + dx,
                    Y = SelectedAnnotation.Record.Rect.Value.Y + dy
                }
            },
            AnnotationKind.Point when SelectedAnnotation.Record.Point is not null => SelectedAnnotation.Record with
            {
                Point = new Point2D(SelectedAnnotation.Record.Point.Value.X + dx, SelectedAnnotation.Record.Point.Value.Y + dy)
            },
            AnnotationKind.Line when SelectedAnnotation.Record.Line is not null => SelectedAnnotation.Record with
            {
                Line = new LineD(
                    new Point2D(SelectedAnnotation.Record.Line.Value.Start.X + dx, SelectedAnnotation.Record.Line.Value.Start.Y + dy),
                    new Point2D(SelectedAnnotation.Record.Line.Value.End.X + dx, SelectedAnnotation.Record.Line.Value.End.Y + dy))
            },
            AnnotationKind.Polygon when SelectedAnnotation.Record.Polygon is not null => SelectedAnnotation.Record with
            {
                Polygon = SelectedAnnotation.Record.Polygon.Select(point => new Point2D(point.X + dx, point.Y + dy)).ToArray()
            },
            _ => SelectedAnnotation.Record
        };

        UpsertAnnotationFromViewport(annotation, isUpdate: true);
    }

    public void AssignSelectedAnnotationLabelByIndex(int labelIndex)
    {
        if (SelectedAnnotation is null || labelIndex < 0 || labelIndex >= Labels.Count)
        {
            return;
        }

        SelectedAnnotationLabel = Labels[labelIndex];
        ApplySelectedAnnotationProperties();
    }

    private void ApplySelectedAnnotationProperties()
    {
        if (SelectedAnnotation is null || SelectedImage is null)
        {
            return;
        }

        try
        {
            var updated = BuildAnnotationFromEditor(SelectedAnnotation.Record);
            UpsertAnnotationFromViewport(updated, isUpdate: true);
            StatusMessage = "Annotation properties applied.";
        }
        catch (FormatException exception)
        {
            StatusMessage = exception.Message;
        }
    }

    private void AcceptSuggestedLabel()
    {
        if (SelectedAnnotation is null || string.IsNullOrWhiteSpace(SelectedAnnotationSuggestedLabel))
        {
            return;
        }

        var suggested = SelectedAnnotationSuggestedLabel.Trim();
        var label = Labels.FirstOrDefault(existing => string.Equals(existing.Name, suggested, StringComparison.OrdinalIgnoreCase));
        if (label is null)
        {
            label = new LabelClass
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = suggested
            };

            ReplaceState(_projectState.ReplaceLabels(_projectState.Labels.Concat([label]).ToArray()));
        }

        SelectedAnnotationLabel = Labels.FirstOrDefault(existing => existing.Id == label.Id);
        SelectedAnnotationSuggestedLabel = string.Empty;
        ApplySelectedAnnotationProperties();
    }

    private void RejectSuggestedLabel()
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        SelectedAnnotationSuggestedLabel = string.Empty;
        ApplySelectedAnnotationProperties();
    }

    private void AcceptAllSuggestions()
    {
        if (SelectedImage is null)
        {
            return;
        }

        var mutableLabels = _projectState.Labels.ToList();
        foreach (var suggestedName in SelectedImage.Annotations
                     .Select(annotation => annotation.SuggestedLabel)
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Select(name => name!)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (mutableLabels.Any(label => string.Equals(label.Name, suggestedName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            mutableLabels.Add(new LabelClass
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = suggestedName
            });
        }

        if (mutableLabels.Count != _projectState.Labels.Count)
        {
            ReplaceState(_projectState.ReplaceLabels(mutableLabels.ToArray()));
        }

        var updated = SelectedImage with
        {
            Annotations = SelectedImage.Annotations
                .Select(annotation =>
                {
                    if (string.IsNullOrWhiteSpace(annotation.SuggestedLabel))
                    {
                        return annotation;
                    }

                    var label = Labels.FirstOrDefault(existing => string.Equals(existing.Name, annotation.SuggestedLabel, StringComparison.OrdinalIgnoreCase));
                    return annotation with
                    {
                        LabelId = label?.Id,
                        SuggestedLabel = null
                    };
                })
                .ToArray()
        };

        ReplaceImage(updated);
        SelectedImage = updated;
        StatusMessage = "All AI suggestions accepted.";
    }

    private void RejectAllSuggestions()
    {
        if (SelectedImage is null)
        {
            return;
        }

        var updated = SelectedImage with
        {
            Annotations = SelectedImage.Annotations
                .Where(annotation => string.IsNullOrWhiteSpace(annotation.SuggestedLabel))
                .ToArray()
        };

        ReplaceImage(updated);
        SelectedImage = updated;
        if (SelectedAnnotation is not null && !updated.Annotations.Any(annotation => annotation.Id == SelectedAnnotation.Record.Id))
        {
            SelectedAnnotation = null;
        }

        StatusMessage = "All AI suggestions rejected.";
    }

    private void AcceptAllDatasetSuggestions()
    {
        var labels = EnsureSuggestedLabelsExist();
        var updatedImages = _projectState.Images
            .Select(image => image with
            {
                Annotations = image.Annotations
                    .Select(annotation =>
                    {
                        if (string.IsNullOrWhiteSpace(annotation.SuggestedLabel))
                        {
                            return annotation;
                        }

                        var label = labels.FirstOrDefault(existing => string.Equals(existing.Name, annotation.SuggestedLabel, StringComparison.OrdinalIgnoreCase));
                        return annotation with
                        {
                            LabelId = label?.Id,
                            SuggestedLabel = null
                        };
                    })
                    .ToArray()
            })
            .ToArray();

        ReplaceState(_projectState.ReplaceImages(updatedImages));
        StatusMessage = "All dataset AI suggestions accepted.";
    }

    private void RejectAllDatasetSuggestions()
    {
        var updatedImages = _projectState.Images
            .Select(image => image with
            {
                Annotations = image.Annotations
                    .Where(annotation => string.IsNullOrWhiteSpace(annotation.SuggestedLabel))
                    .ToArray()
            })
            .ToArray();

        ReplaceState(_projectState.ReplaceImages(updatedImages));
        StatusMessage = "All dataset AI suggestions rejected.";
    }

    private bool ConfirmAcceptAllDatasetSuggestions()
    {
        AcceptAllDatasetSuggestions();
        return true;
    }

    private bool ConfirmRejectAllDatasetSuggestions()
    {
        RejectAllDatasetSuggestions();
        return true;
    }

    private void ToggleSelectedAnnotationVisibility()
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        SelectedAnnotationIsVisible = !SelectedAnnotationIsVisible;
        ApplySelectedAnnotationProperties();
    }

    private void ClearSelectedAnnotationLabel()
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        SelectedAnnotationLabel = null;
        ApplySelectedAnnotationProperties();
    }

    private void ClearNotifications()
    {
        Notifications.Clear();
        ClearNotificationsCommand.RaiseCanExecuteChanged();
    }

    private void ShowDialog(ModalDialogViewModel dialog)
    {
        ActiveDialog = dialog;
    }

    private void CloseDialog(ModalDialogViewModel dialog)
    {
        if (ReferenceEquals(ActiveDialog, dialog))
        {
            ActiveDialog = null;
        }
    }

    private string? BrowseModelPath()
    {
        var initialDirectory = ResolveModelDialogInitialDirectory();
        var dialog = new OpenFileDialog
        {
            Filter = "ONNX Model (*.onnx)|*.onnx",
            Multiselect = false,
            InitialDirectory = initialDirectory
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private string ResolveModelDialogInitialDirectory()
    {
        var selectedModelDirectory = string.IsNullOrWhiteSpace(ActiveModelPath)
            ? null
            : Path.GetDirectoryName(ActiveModelPath);
        if (!string.IsNullOrWhiteSpace(selectedModelDirectory) && Directory.Exists(selectedModelDirectory))
        {
            return selectedModelDirectory;
        }

        return AppContext.BaseDirectory;
    }

    private string[]? BrowseImportPaths(AnnotationFormat format)
    {
        var dialog = new OpenFileDialog
        {
            Multiselect = format is AnnotationFormat.Yolo or AnnotationFormat.Voc,
            Filter = format switch
            {
                AnnotationFormat.Coco => "COCO JSON (*.json)|*.json",
                AnnotationFormat.Yolo => "YOLO package (*.zip)|*.zip|YOLO files (*.txt)|*.txt|All Files (*.*)|*.*",
                AnnotationFormat.Voc => "VOC package (*.zip)|*.zip|VOC XML (*.xml)|*.xml|All Files (*.*)|*.*",
                _ => "All Files (*.*)|*.*"
            }
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : null;
    }

    private string? BrowseExportDestination(AnnotationFormat format)
    {
        var dialog = new SaveFileDialog
        {
            Filter = format switch
            {
                AnnotationFormat.Csv => "CSV (*.csv)|*.csv",
                AnnotationFormat.Coco => "JSON (*.json)|*.json",
                AnnotationFormat.Vgg => "JSON (*.json)|*.json",
                AnnotationFormat.Json => "JSON (*.json)|*.json",
                AnnotationFormat.Yolo => "ZIP (*.zip)|*.zip",
                AnnotationFormat.Voc => "ZIP (*.zip)|*.zip",
                _ => "All Files (*.*)|*.*"
            },
            FileName = $"annotations-{format.ToString().ToLowerInvariant()}{GetDefaultExtension(format)}"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private string[]? LoadLabelNamesFromFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Text files (*.txt;*.csv)|*.txt;*.csv|All Files (*.*)|*.*",
            Multiselect = false
        };

        return dialog.ShowDialog() == true ? File.ReadAllLines(dialog.FileName) : null;
    }

    private bool ConfirmImportAnnotations(ImportAnnotationsDialogViewModel dialog)
    {
        try
        {
            ReplaceState(_annotationImportService.Import(_projectState, dialog.SelectedFormat, dialog.SelectedPaths));
            SelectedImportFormat = dialog.SelectedFormat;
            SelectedImage = _projectState.Images.FirstOrDefault(image => image.Id == SelectedImage?.Id) ?? _projectState.Images.FirstOrDefault();
            SelectedLabel = _projectState.Labels.FirstOrDefault(label => label.Id == SelectedLabel?.Id) ?? _projectState.Labels.FirstOrDefault();
            StatusMessage = $"Imported annotations from {dialog.SelectedFormat}.";
            return true;
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            return false;
        }
    }

    private bool ConfirmExportAnnotations(ExportAnnotationsDialogViewModel dialog)
    {
        try
        {
            _annotationExportService.Export(_projectState, dialog.SelectedFormat, dialog.DestinationPath);
            SelectedExportFormat = dialog.SelectedFormat;
            StatusMessage = $"Exported annotations as {dialog.SelectedFormat}.";
            return true;
        }
        catch (Exception exception)
        {
            StatusMessage = exception.Message;
            return false;
        }
    }

    private bool ConfirmRunInference(InferenceSetupDialogViewModel dialog)
    {
        if (IsInferenceBatchRunning)
        {
            StatusMessage = "Inference is already running on the current dataset.";
            return false;
        }

        if (!File.Exists(dialog.ModelPath))
        {
            StatusMessage = "Select a valid ONNX model file before running inference.";
            return false;
        }

        if (_projectState.Images.Count == 0)
        {
            StatusMessage = "Load at least one image before running inference.";
            return false;
        }

        ActiveModelPath = dialog.ModelPath;
        SelectedInferenceTask = dialog.SelectedInferenceTask;
        ResetInferenceBatchState(_projectState.Images.Count);
        _ = RunInferenceBatchAsync(ActiveModelPath, SelectedInferenceTask, _projectState.Images.ToArray());
        return true;
    }

    private bool ConfirmBulkLabelImport(BulkLabelImportDialogViewModel dialog)
    {
        var parsedLabels = BulkLabelImportDialogViewModel.ParseEntries(dialog.LabelNamesText);
        if (parsedLabels.Count == 0)
        {
            StatusMessage = "Add at least one label before updating the label set.";
            return false;
        }

        if (parsedLabels.SequenceEqual(dialog.OriginalLabels, StringComparer.OrdinalIgnoreCase))
        {
            StatusMessage = "Label set is unchanged.";
            return false;
        }

        var existingByName = _projectState.Labels.ToDictionary(label => label.Name, StringComparer.OrdinalIgnoreCase);
        var updatedLabels = parsedLabels
            .Select(labelName => existingByName.TryGetValue(labelName, out var existingLabel)
                ? existingLabel with { Name = labelName }
                : new LabelClass
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = labelName
                })
            .ToArray();

        var retainedLabelIds = updatedLabels.Select(label => label.Id).ToHashSet(StringComparer.Ordinal);
        var removedLabelIds = _projectState.Labels
            .Where(label => !retainedLabelIds.Contains(label.Id))
            .Select(label => label.Id)
            .ToHashSet(StringComparer.Ordinal);

        var updatedImages = removedLabelIds.Count == 0
            ? _projectState.Images
            : _projectState.Images
                .Select(image => image with
                {
                    Annotations = image.Annotations
                        .Select(annotation => annotation.LabelId is not null && removedLabelIds.Contains(annotation.LabelId)
                            ? annotation with { LabelId = null }
                            : annotation)
                        .ToArray()
                })
                .ToArray();

        ReplaceState(_projectState
            .ReplaceLabels(updatedLabels)
            .ReplaceImages(updatedImages));
        SelectedLabel = Labels.FirstOrDefault(label => string.Equals(label.Name, parsedLabels[0], StringComparison.OrdinalIgnoreCase)) ?? Labels.FirstOrDefault();
        StatusMessage = $"Label set updated. {updatedLabels.Length} active label(s), {removedLabelIds.Count} removed.";
        return true;
    }

    private string BuildDefaultExportPath(AnnotationFormat format)
    {
        var fileName = $"annotations-{format.ToString().ToLowerInvariant()}{GetDefaultExtension(format)}";
        if (!string.IsNullOrWhiteSpace(ProjectPath) &&
            !string.Equals(ProjectPath, "Loose image session", StringComparison.OrdinalIgnoreCase))
        {
            var projectDirectory = Path.GetDirectoryName(ProjectPath);
            if (!string.IsNullOrWhiteSpace(projectDirectory))
            {
                return Path.Combine(projectDirectory, fileName);
            }
        }

        if (SelectedImage is not null)
        {
            var imageDirectory = Path.GetDirectoryName(SelectedImage.Path);
            if (!string.IsNullOrWhiteSpace(imageDirectory))
            {
                return Path.Combine(imageDirectory, fileName);
            }
        }

        return fileName;
    }

    private IReadOnlyList<LabelClass> EnsureSuggestedLabelsExist()
    {
        var mutableLabels = _projectState.Labels.ToList();
        foreach (var suggestedName in _projectState.Images
                     .SelectMany(image => image.Annotations)
                     .Select(annotation => annotation.SuggestedLabel)
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Select(name => name!)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (mutableLabels.Any(label => string.Equals(label.Name, suggestedName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            mutableLabels.Add(new LabelClass
            {
                Id = Guid.NewGuid().ToString("N"),
                Name = suggestedName
            });
        }

        if (mutableLabels.Count != _projectState.Labels.Count)
        {
            ReplaceState(_projectState.ReplaceLabels(mutableLabels.ToArray()));
        }

        return Labels.ToArray();
    }

    private void ReplaceState(ProjectState state)
    {
        var preferredImageId = _selectedImage?.Id ?? state.ActiveImageId;
        var preferredLabelId = _selectedLabel?.Id;
        var preferredAnnotationId = _selectedAnnotation?.Record.Id;

        _projectState = state;

        SyncCollection(Images, _projectState.Images);
        SyncCollection(Labels, _projectState.Labels);
        ApplySelectedImage(
            ResolveImage(preferredImageId) ?? ResolveImage(_projectState.ActiveImageId) ?? Images.FirstOrDefault(),
            updateProjectState: false,
            updateNativeEngine: false);
        SelectedLabel = preferredLabelId is null
            ? Labels.FirstOrDefault()
            : Labels.FirstOrDefault(label => label.Id == preferredLabelId) ?? Labels.FirstOrDefault();
        RefreshAnnotationCollection();
        SelectedAnnotation = preferredAnnotationId is null
            ? null
            : Annotations.FirstOrDefault(item => item.Record.Id == preferredAnnotationId);
        RaisePropertyChanged(nameof(SelectedImageSummary));
        RaisePropertyChanged(nameof(CurrentAnnotations));
        RaisePropertyChanged(nameof(SelectedAnnotationId));
        RaisePropertyChanged(nameof(AnnotationLabelOptions));
        RaisePropertyChanged(nameof(SuggestedAnnotationCount));
        RaisePropertyChanged(nameof(FilteredImageCount));
        RaisePropertyChanged(nameof(SuggestionImageCount));
        RaisePropertyChanged(nameof(PendingReviewImageCount));
        RaisePropertyChanged(nameof(HasImagesLoaded));
        RaisePropertyChanged(nameof(HasFilteredImages));
        RaisePropertyChanged(nameof(EmptyStateMessage));
        RaisePropertyChanged(nameof(EmptyStateHint));
        RaisePropertyChanged(nameof(ProjectName));
        RaisePropertyChanged(nameof(ProjectProgressPercent));
        RefreshImageBrowser();
        ImportAnnotationsCommand.RaiseCanExecuteChanged();
        ExportAnnotationsCommand.RaiseCanExecuteChanged();
        AddAnnotationCommand.RaiseCanExecuteChanged();
        DeleteAnnotationCommand.RaiseCanExecuteChanged();
        RemoveLabelCommand.RaiseCanExecuteChanged();
        AddImagesCommand.RaiseCanExecuteChanged();
        PreviousImageCommand.RaiseCanExecuteChanged();
        NextImageCommand.RaiseCanExecuteChanged();
        NextPendingReviewImageCommand.RaiseCanExecuteChanged();
        NextSuggestionImageCommand.RaiseCanExecuteChanged();
        AcceptAllSuggestionsCommand.RaiseCanExecuteChanged();
        RejectAllSuggestionsCommand.RaiseCanExecuteChanged();
        AcceptAllDatasetSuggestionsCommand.RaiseCanExecuteChanged();
        RejectAllDatasetSuggestionsCommand.RaiseCanExecuteChanged();
        ClearNotificationsCommand.RaiseCanExecuteChanged();
    }

    private void RefreshAnnotationCollection()
    {
        Annotations.Clear();
        if (SelectedImage is null)
        {
            return;
        }

        var labels = _projectState.Labels.ToDictionary(label => label.Id, label => label.Name);
        foreach (var annotation in SelectedImage.Annotations)
        {
            Annotations.Add(new AnnotationItemViewModel(annotation, labels));
        }

        if (SelectedAnnotation is not null)
        {
            SelectedAnnotation = Annotations.FirstOrDefault(item => item.Record.Id == SelectedAnnotation.Record.Id);
        }
    }

    private void ApplyToolFormatDefaults()
    {
        if (AvailableImportFormats.Count > 0)
        {
            SelectedImportFormat = AvailableImportFormats[0];
        }

        if (AvailableExportFormats.Count > 0)
        {
            SelectedExportFormat = AvailableExportFormats[0];
        }
    }

    private void OnSessionStatusChanged(object? sender, string status)
    {
        if (_uiDispatcher.CheckAccess())
        {
            StatusMessage = status;
            return;
        }

        _ = _uiDispatcher.BeginInvoke(() => StatusMessage = status);
    }

    private void OnNotificationTimerTick(object? sender, EventArgs e)
    {
        var cutoff = DateTimeOffset.Now.AddSeconds(-8);
        for (var index = Notifications.Count - 1; index >= 0; index--)
        {
            if (Notifications[index].CreatedAt >= cutoff)
            {
                continue;
            }

            Notifications.RemoveAt(index);
        }

        ClearNotificationsCommand.RaiseCanExecuteChanged();
    }

    private void RefreshImageBrowser()
    {
        FilteredImagesView.Refresh();
        var visibleImages = FilteredImagesView.Cast<ImageRecord>().ToArray();
        if (SelectedImage is null && visibleImages.Length > 0)
        {
            ApplySelectedImage(
                visibleImages.FirstOrDefault(),
                updateProjectState: true,
                updateNativeEngine: true);
        }

        RaisePropertyChanged(nameof(FilteredImageCount));
        RaisePropertyChanged(nameof(HasImagesLoaded));
        RaisePropertyChanged(nameof(HasFilteredImages));
        RaisePropertyChanged(nameof(EmptyStateMessage));
        RaisePropertyChanged(nameof(EmptyStateHint));
        NextPendingReviewImageCommand.RaiseCanExecuteChanged();
        NextSuggestionImageCommand.RaiseCanExecuteChanged();
    }

    private bool FilterImage(object item)
    {
        if (item is not ImageRecord image)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ImageSearchQuery) &&
            !image.FileName.Contains(ImageSearchQuery, StringComparison.OrdinalIgnoreCase) &&
            !Path.GetFileNameWithoutExtension(image.FileName).Contains(ImageSearchQuery, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return SelectedImageFilter switch
        {
            ImageFilterMode.CurrentToolPending => ImageNeedsReview(image),
            ImageFilterMode.CurrentToolAnnotated => ImageHasCurrentToolAnnotations(image),
            ImageFilterMode.WithSuggestions => ImageHasSuggestions(image),
            _ => true
        };
    }

    private void PushNotification(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var normalized = message.Trim();
        if (Notifications.Count > 0 && string.Equals(Notifications[0].Message, normalized, StringComparison.Ordinal))
        {
            Notifications[0].CreatedAt = DateTimeOffset.Now;
            return;
        }

        Notifications.Insert(0, new NotificationEntryViewModel(normalized));
        while (Notifications.Count > 5)
        {
            Notifications.RemoveAt(Notifications.Count - 1);
        }

        ClearNotificationsCommand.RaiseCanExecuteChanged();
    }

    private void ApplySelectedImage(ImageRecord? value, bool updateProjectState, bool updateNativeEngine)
    {
        var previousImage = _selectedImage;
        if (!SetProperty(ref _selectedImage, value, nameof(SelectedImage)))
        {
            if (updateProjectState && _projectState.ActiveImageId != value?.Id)
            {
                _projectState = _projectState.SetActiveImage(value?.Id);
            }

            if (updateNativeEngine && value is null)
            {
                EngineSession.SetActiveImage(string.Empty);
                UpdateViewportTransform(1.0, 0.0, 0.0);
            }

            return;
        }

        if (updateProjectState)
        {
            _projectState = _projectState.SetActiveImage(value?.Id);
        }

        RaisePropertyChanged(nameof(SelectedImageSummary));
        RaisePropertyChanged(nameof(CurrentAnnotations));
        RaisePropertyChanged(nameof(HasFilteredImages));
        RefreshAnnotationCollection();
        PreviousImageCommand.RaiseCanExecuteChanged();
        NextImageCommand.RaiseCanExecuteChanged();
        AddAnnotationCommand.RaiseCanExecuteChanged();
        DeleteAnnotationCommand.RaiseCanExecuteChanged();

        if (updateNativeEngine)
        {
            var previousPath = previousImage?.Path ?? string.Empty;
            var nextPath = value?.Path ?? string.Empty;
            if (!string.Equals(previousPath, nextPath, StringComparison.OrdinalIgnoreCase))
            {
                EngineSession.SetActiveImage(nextPath);
                UpdateViewportTransform(1.0, 0.0, 0.0);
            }
        }
    }

    private ImageRecord? ResolveImage(string? imageId)
    {
        return imageId is null ? null : _projectState.Images.FirstOrDefault(image => image.Id == imageId);
    }

    private bool ImageHasCurrentToolAnnotations(ImageRecord image)
    {
        var targetKind = MapTool(SelectedTool);
        return targetKind switch
        {
            AnnotationKind.ImageRecognition => image.Annotations.Any(annotation => annotation.Kind == AnnotationKind.ImageRecognition && annotation.LabelId is not null),
            _ => image.Annotations.Any(annotation => annotation.Kind == targetKind)
        };
    }

    private bool ImageHasSuggestions(ImageRecord image)
    {
        return image.Annotations.Any(annotation => !string.IsNullOrWhiteSpace(annotation.SuggestedLabel));
    }

    private bool ImageNeedsReview(ImageRecord image)
    {
        return ImageHasSuggestions(image) || !ImageHasCurrentToolAnnotations(image);
    }

    private void SelectImageByOffset(int offset)
    {
        if (Images.Count == 0)
        {
            return;
        }

        var selectedIndex = SelectedImage is null ? -1 : Images.IndexOf(SelectedImage);
        var targetIndex = Math.Clamp(selectedIndex + offset, 0, Images.Count - 1);
        SelectedImage = Images[targetIndex];
        StatusMessage = $"Active image: {SelectedImage.FileName} ({targetIndex + 1}/{Images.Count}).";
    }

    private void SelectNextImageMatching(Func<ImageRecord, bool> predicate, string fallbackMessage)
    {
        if (Images.Count == 0)
        {
            return;
        }

        var selectedIndex = SelectedImage is null ? -1 : Images.IndexOf(SelectedImage);
        for (var step = 1; step <= Images.Count; step++)
        {
            var candidateIndex = (selectedIndex + step + Images.Count) % Images.Count;
            var candidate = Images[candidateIndex];
            if (!predicate(candidate))
            {
                continue;
            }

            SelectedImage = candidate;
            StatusMessage = $"Active image: {candidate.FileName} ({candidateIndex + 1}/{Images.Count}).";
            return;
        }

        StatusMessage = fallbackMessage;
    }

    private bool CanSelectPreviousImage()
    {
        return SelectedImage is not null && Images.IndexOf(SelectedImage) > 0;
    }

    private bool CanSelectNextImage()
    {
        return SelectedImage is not null && Images.IndexOf(SelectedImage) >= 0 && Images.IndexOf(SelectedImage) < Images.Count - 1;
    }

    private void LoadSelectedAnnotationEditor()
    {
        var record = SelectedAnnotation?.Record;
        SelectedAnnotationLabel = record?.LabelId is null
            ? null
            : Labels.FirstOrDefault(label => label.Id == record.LabelId);
        SelectedAnnotationSuggestedLabel = record?.SuggestedLabel ?? string.Empty;
        SelectedAnnotationIsVisible = record?.IsVisible ?? true;

        RectX = record?.Rect is not null ? FormatNumber(record.Rect.Value.X) : string.Empty;
        RectY = record?.Rect is not null ? FormatNumber(record.Rect.Value.Y) : string.Empty;
        RectWidth = record?.Rect is not null ? FormatNumber(record.Rect.Value.Width) : string.Empty;
        RectHeight = record?.Rect is not null ? FormatNumber(record.Rect.Value.Height) : string.Empty;
        PointX = record?.Point is not null ? FormatNumber(record.Point.Value.X) : string.Empty;
        PointY = record?.Point is not null ? FormatNumber(record.Point.Value.Y) : string.Empty;
        LineStartX = record?.Line is not null ? FormatNumber(record.Line.Value.Start.X) : string.Empty;
        LineStartY = record?.Line is not null ? FormatNumber(record.Line.Value.Start.Y) : string.Empty;
        LineEndX = record?.Line is not null ? FormatNumber(record.Line.Value.End.X) : string.Empty;
        LineEndY = record?.Line is not null ? FormatNumber(record.Line.Value.End.Y) : string.Empty;
        PolygonPointsText = record?.Polygon is not null
            ? string.Join(Environment.NewLine, record.Polygon.Select(point => $"{FormatNumber(point.X)},{FormatNumber(point.Y)}"))
            : string.Empty;
    }

    private async Task RunInferenceBatchAsync(string modelPath, string taskName, IReadOnlyList<ImageRecord> images)
    {
        IsInferenceBatchRunning = true;
        RaisePropertyChanged(nameof(InferenceBatchProgressPercent));
        RaisePropertyChanged(nameof(InferenceBatchProgressLabel));

        try
        {
            var originalSelectedImagePath = SelectedImage?.Path ?? string.Empty;
            for (var index = 0; index < images.Count; index++)
            {
                var image = images[index];
                await RunInferenceForImageAsync(modelPath, taskName, image.Path);
                _inferenceBatchCompletedImages = index + 1;
                RaisePropertyChanged(nameof(InferenceBatchProgressPercent));
                RaisePropertyChanged(nameof(InferenceBatchProgressLabel));
            }

            await _uiDispatcher.InvokeAsync(() =>
            {
                if (!string.IsNullOrWhiteSpace(originalSelectedImagePath))
                {
                    EngineSession.SetActiveImage(originalSelectedImagePath);
                }

                var imagesWithSuggestions = SuggestionImageCount;
                StatusMessage = $"Batch inference completed. {imagesWithSuggestions} image(s) now contain suggestions.";
            });
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(() =>
            {
                IsInferenceBatchRunning = false;
                RaisePropertyChanged(nameof(InferenceBatchProgressPercent));
                RaisePropertyChanged(nameof(InferenceBatchProgressLabel));
            });
        }
    }

    private async Task RunInferenceForImageAsync(string modelPath, string taskName, string imagePath)
    {
        try
        {
            await Task.Run(() =>
            {
                EngineSession.SetActiveImage(imagePath);
                EngineSession.RunInference(modelPath, taskName);
            });
            await _uiDispatcher.InvokeAsync(() =>
            {
                ApplyNativeInferenceSuggestions(modelPath, taskName, imagePath);
                var selectedImagePath = SelectedImage?.Path ?? string.Empty;
                if (!string.Equals(selectedImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
                {
                    EngineSession.SetActiveImage(selectedImagePath);
                }
            });
        }
        catch (Exception exception)
        {
            await _uiDispatcher.InvokeAsync(() => StatusMessage = exception.Message);
        }
    }

    private void ApplyNativeInferenceSuggestions(string modelPath, string taskName, string imagePath)
    {
        var summary = EngineSession.ReadLatestInferenceSummary();
        UpdateInferenceRuntime(summary);
        if (summary is null ||
            !string.Equals(summary.ModelPath, modelPath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(summary.TaskName, taskName, StringComparison.Ordinal) ||
            !string.Equals(summary.ActiveImagePath, imagePath, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = $"Inference requested for {Path.GetFileName(modelPath)} on {Path.GetFileName(imagePath)}. No native result available.";
            return;
        }

        var suggestions = EngineSession.ReadLatestInferenceSuggestions();
        var targetImage = _projectState.Images.FirstOrDefault(image => string.Equals(image.Path, summary.ActiveImagePath, StringComparison.OrdinalIgnoreCase));
        if (targetImage is null)
        {
            StatusMessage = $"Inference completed for {Path.GetFileName(modelPath)}, but the target image is no longer loaded in the project.";
            return;
        }

        if (summary.ResultCode != MsInferenceResultCode.Ok)
        {
            StatusMessage = string.IsNullOrWhiteSpace(summary.StatusMessage)
                ? $"Inference failed on {summary.ProviderName}."
                : summary.StatusMessage;
            return;
        }

        if (suggestions.Count == 0)
        {
            StatusMessage = string.IsNullOrWhiteSpace(summary.StatusMessage)
                ? $"Inference completed on {summary.ProviderName} with no supported suggestions."
                : summary.StatusMessage;
            return;
        }

        var updatedImage = targetImage with
        {
            Annotations = targetImage.Annotations
                .Where(annotation => string.IsNullOrWhiteSpace(annotation.SuggestedLabel))
                .Concat(suggestions.Select(ConvertNativeSuggestion))
                .ToArray()
        };

        ReplaceImage(updatedImage);
        if (SelectedImage?.Id == updatedImage.Id)
        {
            SelectedAnnotation = Annotations.FirstOrDefault(annotation => !string.IsNullOrWhiteSpace(annotation.Record.SuggestedLabel));
        }

        StatusMessage = $"{suggestions.Count} native suggestion(s) loaded for {Path.GetFileName(summary.ActiveImagePath)} via {summary.ProviderName}.";
    }

    private void ResetInferenceBatchState(int totalImages = 0)
    {
        _inferenceBatchCompletedImages = 0;
        _inferenceBatchTotalImages = totalImages;
        IsInferenceBatchRunning = false;
        RaisePropertyChanged(nameof(InferenceBatchProgressPercent));
        RaisePropertyChanged(nameof(InferenceBatchProgressLabel));
    }

    private void UpdateInferenceRuntime(NativeInferenceResultSummary? summary)
    {
        if (summary is null)
        {
            ActiveInferenceBackend = "Not run";
            ActiveInferenceProvider = "N/A";
            ActiveInferenceRuntime = "Native inference idle.";
            return;
        }

        ActiveInferenceBackend = string.IsNullOrWhiteSpace(summary.BackendName) ? "Unavailable" : summary.BackendName;
        ActiveInferenceProvider = string.IsNullOrWhiteSpace(summary.ProviderName) ? "N/A" : summary.ProviderName;
        ActiveInferenceRuntime = string.IsNullOrWhiteSpace(summary.StatusMessage)
            ? $"Backend {ActiveInferenceBackend} via {ActiveInferenceProvider}."
            : summary.StatusMessage;
    }

    private static AnnotationRecord ConvertNativeSuggestion(NativeInferenceSuggestion suggestion)
    {
        return suggestion.Kind switch
        {
            MsInferenceSuggestionKind.Point => new AnnotationRecord
            {
                Id = suggestion.Id,
                Kind = AnnotationKind.Point,
                IsVisible = suggestion.IsVisible,
                Point = new Point2D(suggestion.PointX, suggestion.PointY),
                SuggestedLabel = string.IsNullOrWhiteSpace(suggestion.SuggestedLabel) ? null : suggestion.SuggestedLabel.Trim()
            },
            _ => new AnnotationRecord
            {
                Id = suggestion.Id,
                Kind = AnnotationKind.Rect,
                IsVisible = suggestion.IsVisible,
                Rect = new RectD(suggestion.RectX, suggestion.RectY, suggestion.RectWidth, suggestion.RectHeight),
                SuggestedLabel = string.IsNullOrWhiteSpace(suggestion.SuggestedLabel) ? null : suggestion.SuggestedLabel.Trim()
            }
        };
    }

    private AnnotationRecord BuildAnnotationFromEditor(AnnotationRecord source)
    {
        var updated = source with
        {
            LabelId = SelectedAnnotationLabel?.Id,
            IsVisible = SelectedAnnotationIsVisible,
            SuggestedLabel = string.IsNullOrWhiteSpace(SelectedAnnotationSuggestedLabel) ? null : SelectedAnnotationSuggestedLabel.Trim()
        };

        return source.Kind switch
        {
            AnnotationKind.Rect => updated with
            {
                Rect = new RectD(
                    ParseRequiredDouble(RectX, "Rect X"),
                    ParseRequiredDouble(RectY, "Rect Y"),
                    Math.Max(0, ParseRequiredDouble(RectWidth, "Rect Width")),
                    Math.Max(0, ParseRequiredDouble(RectHeight, "Rect Height")))
            },
            AnnotationKind.Point => updated with
            {
                Point = new Point2D(
                    ParseRequiredDouble(PointX, "Point X"),
                    ParseRequiredDouble(PointY, "Point Y"))
            },
            AnnotationKind.Line => updated with
            {
                Line = new LineD(
                    new Point2D(ParseRequiredDouble(LineStartX, "Line Start X"), ParseRequiredDouble(LineStartY, "Line Start Y")),
                    new Point2D(ParseRequiredDouble(LineEndX, "Line End X"), ParseRequiredDouble(LineEndY, "Line End Y")))
            },
            AnnotationKind.Polygon => updated with
            {
                Polygon = ParsePolygonPoints(PolygonPointsText)
            },
            _ => updated
        };
    }

    private static IReadOnlyList<Point2D> ParsePolygonPoints(string value)
    {
        var lines = value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length < 3)
        {
            throw new FormatException("Polygon requires at least 3 points in 'x,y' format.");
        }

        var points = new List<Point2D>(lines.Length);
        foreach (var line in lines)
        {
            var parts = line.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                throw new FormatException($"Invalid polygon point '{line}'. Expected 'x,y'.");
            }

            points.Add(new Point2D(
                ParseRequiredDouble(parts[0], "Polygon X"),
                ParseRequiredDouble(parts[1], "Polygon Y")));
        }

        return points;
    }

    private static double ParseRequiredDouble(string value, string fieldName)
    {
        if (!double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed) &&
            !double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out parsed))
        {
            throw new FormatException($"{fieldName} must be a valid number.");
        }

        return parsed;
    }

    private static string FormatNumber(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static AnnotationKind MapTool(AnnotationTool tool)
    {
        return tool switch
        {
            AnnotationTool.Rect => AnnotationKind.Rect,
            AnnotationTool.Point => AnnotationKind.Point,
            AnnotationTool.Line => AnnotationKind.Line,
            AnnotationTool.Polygon => AnnotationKind.Polygon,
            AnnotationTool.ImageRecognition => AnnotationKind.ImageRecognition,
            _ => AnnotationKind.Rect
        };
    }

    private static AnnotationRecord CreateDefaultAnnotation(AnnotationTool tool, Size2D? imageSize, string? labelId)
    {
        var width = imageSize?.Width ?? 1024;
        var height = imageSize?.Height ?? 768;
        var center = new Point2D(width / 2.0, height / 2.0);

        return tool switch
        {
            AnnotationTool.Rect => new AnnotationRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = AnnotationKind.Rect,
                LabelId = labelId,
                Rect = new RectD(Math.Max(0, center.X - 80), Math.Max(0, center.Y - 60), 160, 120)
            },
            AnnotationTool.Point => new AnnotationRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = AnnotationKind.Point,
                LabelId = labelId,
                Point = center
            },
            AnnotationTool.Line => new AnnotationRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = AnnotationKind.Line,
                LabelId = labelId,
                Line = new LineD(new Point2D(Math.Max(0, center.X - 100), Math.Max(0, center.Y - 40)),
                    new Point2D(Math.Min(width, center.X + 100), Math.Min(height, center.Y + 40)))
            },
            AnnotationTool.Polygon => new AnnotationRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = AnnotationKind.Polygon,
                LabelId = labelId,
                Polygon =
                [
                    new Point2D(Math.Max(0, center.X - 80), Math.Min(height, center.Y + 60)),
                    new Point2D(center.X, Math.Max(0, center.Y - 80)),
                    new Point2D(Math.Min(width, center.X + 80), Math.Min(height, center.Y + 60))
                ]
            },
            AnnotationTool.ImageRecognition => new AnnotationRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = AnnotationKind.ImageRecognition,
                LabelId = labelId
            },
            _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, null)
        };
    }

    private static void SyncCollection<T>(ObservableCollection<T> collection, IEnumerable<T> items)
    {
        collection.Clear();
        foreach (var item in items)
        {
            collection.Add(item);
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
            _ => ".dat"
        };
    }
}
