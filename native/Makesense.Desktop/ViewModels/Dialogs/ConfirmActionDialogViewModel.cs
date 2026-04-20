using Makesense.Desktop.Commands;

namespace Makesense.Desktop.ViewModels.Dialogs;

public sealed class ConfirmActionDialogViewModel : ModalDialogViewModel
{
    private readonly Func<bool> _confirmAction;

    public ConfirmActionDialogViewModel(
        string title,
        string description,
        string actionText,
        Func<bool> confirmAction,
        Action<ModalDialogViewModel> closeAction)
        : base(title, description, closeAction)
    {
        ActionText = actionText;
        _confirmAction = confirmAction;
        ConfirmCommand = new RelayCommand(Confirm);
    }

    public string ActionText { get; }

    public RelayCommand ConfirmCommand { get; }

    private void Confirm()
    {
        if (_confirmAction())
        {
            RequestClose();
        }
    }
}
