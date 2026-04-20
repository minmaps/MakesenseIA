using Makesense.Desktop.Commands;

namespace Makesense.Desktop.ViewModels.Dialogs;

public abstract class ModalDialogViewModel : ViewModelBase
{
    private readonly Action<ModalDialogViewModel> _closeAction;

    protected ModalDialogViewModel(string title, string description, Action<ModalDialogViewModel> closeAction)
    {
        Title = title;
        Description = description;
        _closeAction = closeAction;
        CancelCommand = new RelayCommand(RequestClose);
    }

    public string Title { get; }

    public string Description { get; }

    public RelayCommand CancelCommand { get; }

    protected void RequestClose() => _closeAction(this);
}
