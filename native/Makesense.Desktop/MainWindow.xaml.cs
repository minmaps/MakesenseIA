using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Makesense.Desktop.Controls;
using Makesense.Desktop.ViewModels;
using System.ComponentModel;
using System.Windows.Threading;

namespace Makesense.Desktop;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Closed += OnClosed;
        EditorOverlay.AnnotationCreated += OnAnnotationCreated;
        EditorOverlay.AnnotationUpdated += OnAnnotationUpdated;
        EditorOverlay.AnnotationSelectionChanged += OnAnnotationSelectionChanged;
        EditorOverlay.ViewportPointerMoved += OnViewportPointerMoved;
        EditorOverlay.ViewportTransformChanged += OnViewportTransformChanged;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as MainWindowViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        RenderHost.Session = _viewModel?.EngineSession;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        EditorOverlay.AnnotationCreated -= OnAnnotationCreated;
        EditorOverlay.AnnotationUpdated -= OnAnnotationUpdated;
        EditorOverlay.AnnotationSelectionChanged -= OnAnnotationSelectionChanged;
        EditorOverlay.ViewportPointerMoved -= OnViewportPointerMoved;
        EditorOverlay.ViewportTransformChanged -= OnViewportTransformChanged;
        PreviewKeyDown -= OnPreviewKeyDown;

        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsDialogOpen) || _viewModel?.IsDialogOpen != false)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (_viewModel?.SelectedImage is not null)
            {
                EditorOverlay.Focus();
            }
        }, DispatcherPriority.Input);
    }

    private void OnAnnotationCreated(object? sender, AnnotationRecordEventArgs e)
    {
        (DataContext as MainWindowViewModel)?.UpsertAnnotationFromViewport(e.Annotation, isUpdate: false);
    }

    private void OnAnnotationUpdated(object? sender, AnnotationRecordEventArgs e)
    {
        (DataContext as MainWindowViewModel)?.UpsertAnnotationFromViewport(e.Annotation, isUpdate: true);
    }

    private void OnAnnotationSelectionChanged(object? sender, AnnotationSelectionEventArgs e)
    {
        (DataContext as MainWindowViewModel)?.SelectAnnotationById(e.AnnotationId);
    }

    private void OnViewportPointerMoved(object? sender, ViewportPointerEventArgs e)
    {
        (DataContext as MainWindowViewModel)?.ForwardViewportPointer(e.ViewportPoint);
    }

    private void OnViewportTransformChanged(object? sender, ViewportTransformChangedEventArgs e)
    {
        (DataContext as MainWindowViewModel)?.UpdateViewportTransform(e.Zoom, e.PanOffsetX, e.PanOffsetY);
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.IsDialogOpen)
        {
            if (e.Key == Key.Escape && viewModel.ActiveDialog?.CancelCommand.CanExecute(null) == true)
            {
                viewModel.ActiveDialog.CancelCommand.Execute(null);
                e.Handled = true;
            }

            return;
        }

        if (TryHandleFileShortcut(e, viewModel))
        {
            return;
        }

        if (IsTextInputSource(e.OriginalSource))
        {
            return;
        }

        if (e.Key == System.Windows.Input.Key.Delete || e.Key == System.Windows.Input.Key.Back)
        {
            viewModel.DeleteSelectedAnnotationFromViewport();
            e.Handled = true;
            return;
        }

        if (e.Key == System.Windows.Input.Key.Escape)
        {
            EditorOverlay.CancelPendingGeometry();
            e.Handled = true;
            return;
        }

        if (e.Key == System.Windows.Input.Key.Enter)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                viewModel.ApplySelectedAnnotationPropertiesCommand.Execute(null);
            }
            else
            {
                EditorOverlay.FinalizePendingPolygon();
            }

            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == System.Windows.Input.ModifierKeys.Control)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                if (e.Key == Key.A)
                {
                    viewModel.AcceptAllDatasetSuggestionsCommand.Execute(null);
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.R)
                {
                    viewModel.RejectAllDatasetSuggestionsCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }

            if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt)
            {
                if (e.Key == Key.A)
                {
                    viewModel.AcceptAllSuggestionsCommand.Execute(null);
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.R)
                {
                    viewModel.RejectAllSuggestionsCommand.Execute(null);
                    e.Handled = true;
                    return;
                }
            }

            var numericLabelIndex = e.Key switch
            {
                Key.D1 or Key.NumPad1 => 0,
                Key.D2 or Key.NumPad2 => 1,
                Key.D3 or Key.NumPad3 => 2,
                Key.D4 or Key.NumPad4 => 3,
                Key.D5 or Key.NumPad5 => 4,
                Key.D6 or Key.NumPad6 => 5,
                Key.D7 or Key.NumPad7 => 6,
                Key.D8 or Key.NumPad8 => 7,
                Key.D9 or Key.NumPad9 => 8,
                _ => -1
            };

            if (numericLabelIndex >= 0)
            {
                viewModel.AssignSelectedAnnotationLabelByIndex(numericLabelIndex);
                e.Handled = true;
                return;
            }

            if (e.Key is System.Windows.Input.Key.OemPlus or System.Windows.Input.Key.Add)
            {
                EditorOverlay.ZoomAtCenter(1.1);
                e.Handled = true;
                return;
            }

            if (e.Key is System.Windows.Input.Key.OemMinus or System.Windows.Input.Key.Subtract)
            {
                EditorOverlay.ZoomAtCenter(1.0 / 1.1);
                e.Handled = true;
                return;
            }

            if (e.Key == System.Windows.Input.Key.D0)
            {
                viewModel.ResetViewport();
                e.Handled = true;
                return;
            }

            if (e.Key == System.Windows.Input.Key.PageUp)
            {
                viewModel.SelectPreviousImage();
                e.Handled = true;
                return;
            }

            if (e.Key == System.Windows.Input.Key.PageDown)
            {
                viewModel.SelectNextImage();
                e.Handled = true;
                return;
            }
        }

        if ((Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0)
        {
            switch (e.Key)
            {
                case System.Windows.Input.Key.Left:
                    viewModel.SelectPreviousImage();
                    e.Handled = true;
                    return;
                case System.Windows.Input.Key.Right:
                    if (viewModel.AcceptAllSuggestionsCommand.CanExecute(null))
                    {
                        viewModel.AcceptAllSuggestionsCommand.Execute(null);
                    }

                    viewModel.SelectNextImage();
                    e.Handled = true;
                    return;
            }
        }

        var step = (Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift ? 10.0 : 1.0;
        switch (e.Key)
        {
            case System.Windows.Input.Key.Left:
                viewModel.NudgeSelectedAnnotation(-step, 0);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Right:
                viewModel.NudgeSelectedAnnotation(step, 0);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Up:
                viewModel.NudgeSelectedAnnotation(0, -step);
                e.Handled = true;
                break;
            case System.Windows.Input.Key.Down:
                viewModel.NudgeSelectedAnnotation(0, step);
                e.Handled = true;
                break;
        }
    }

    private static bool TryHandleFileShortcut(KeyEventArgs e, MainWindowViewModel viewModel)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
        {
            return false;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift && e.Key == Key.S)
        {
            if (viewModel.SaveProjectAsCommand.CanExecute(null))
            {
                viewModel.SaveProjectAsCommand.Execute(null);
            }

            e.Handled = true;
            return true;
        }

        switch (e.Key)
        {
            case Key.N:
                if (viewModel.NewProjectCommand.CanExecute(null))
                {
                    viewModel.NewProjectCommand.Execute(null);
                }

                e.Handled = true;
                return true;
            case Key.O:
                if (viewModel.OpenProjectCommand.CanExecute(null))
                {
                    viewModel.OpenProjectCommand.Execute(null);
                }

                e.Handled = true;
                return true;
            case Key.S:
                if (viewModel.SaveProjectCommand.CanExecute(null))
                {
                    viewModel.SaveProjectCommand.Execute(null);
                }

                e.Handled = true;
                return true;
            default:
                return false;
        }
    }

    private static bool IsTextInputSource(object? originalSource)
    {
        return originalSource is TextBox
            or RichTextBox
            or PasswordBox
            or ComboBox;
    }
}
