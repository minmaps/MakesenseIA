using Makesense.Desktop.Commands;

namespace Makesense.Desktop.ViewModels.Dialogs;

public sealed class InferenceSetupDialogViewModel : ModalDialogViewModel
{
    private readonly Func<string?> _browseModelPath;
    private readonly Func<InferenceSetupDialogViewModel, bool> _confirmAction;
    private string _modelPath;
    private string _selectedInferenceTask;

    public InferenceSetupDialogViewModel(
        IReadOnlyList<string> availableInferenceTasks,
        string selectedInferenceTask,
        string modelPath,
        Func<string?> browseModelPath,
        Func<InferenceSetupDialogViewModel, bool> confirmAction,
        Action<ModalDialogViewModel> closeAction)
        : base(
            "Run Local Inference",
            "Choose the ONNX model and task before sending work to the native runtime.",
            closeAction)
    {
        AvailableInferenceTasks = availableInferenceTasks;
        _selectedInferenceTask = selectedInferenceTask;
        _modelPath = modelPath;
        _browseModelPath = browseModelPath;
        _confirmAction = confirmAction;
        BrowseModelCommand = new RelayCommand(BrowseModel);
        ConfirmCommand = new RelayCommand(Confirm, CanConfirm);
    }

    public IReadOnlyList<string> AvailableInferenceTasks { get; }

    public RelayCommand BrowseModelCommand { get; }

    public RelayCommand ConfirmCommand { get; }

    public string ModelPath
    {
        get => _modelPath;
        set
        {
            if (SetProperty(ref _modelPath, value))
            {
                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedInferenceTask
    {
        get => _selectedInferenceTask;
        set
        {
            if (SetProperty(ref _selectedInferenceTask, value))
            {
                ConfirmCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private void BrowseModel()
    {
        var selectedPath = _browseModelPath();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            ModelPath = selectedPath;
        }
    }

    private bool CanConfirm()
    {
        return !string.IsNullOrWhiteSpace(ModelPath) && !string.IsNullOrWhiteSpace(SelectedInferenceTask);
    }

    private void Confirm()
    {
        if (_confirmAction(this))
        {
            RequestClose();
        }
    }
}
