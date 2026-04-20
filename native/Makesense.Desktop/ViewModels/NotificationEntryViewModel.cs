namespace Makesense.Desktop.ViewModels;

public sealed class NotificationEntryViewModel : ViewModelBase
{
    private string _message;
    private DateTimeOffset _createdAt;

    public NotificationEntryViewModel(string message)
    {
        Id = Guid.NewGuid().ToString("N");
        _message = message;
        _createdAt = DateTimeOffset.Now;
    }

    public string Id { get; }

    public string Message
    {
        get => _message;
        set => SetProperty(ref _message, value);
    }

    public DateTimeOffset CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value);
    }
}
