using Makesense.Desktop.ViewModels.Dialogs;
using Makesense.Formats.Contracts;

namespace Makesense.Desktop.Tests;

public sealed class WorkflowDialogViewModelTests
{
    [Fact]
    public void BulkLabelImportDialogViewModel_ParseEntries_TrimsAndDeduplicatesLabels()
    {
        var parsed = BulkLabelImportDialogViewModel.ParseEntries(" car \r\nperson\ncar;bike,, person ");

        Assert.Equal(["car", "person", "bike"], parsed);
    }

    [Fact]
    public void BulkLabelImportDialogViewModel_DisablesConfirmWhenLabelSetIsUnchanged()
    {
        var dialog = new BulkLabelImportDialogViewModel(
            ["car", "person"],
            () => null,
            _ => true,
            _ => { });

        Assert.Equal("car" + Environment.NewLine + "person", dialog.LabelNamesText);
        Assert.False(dialog.HasChanges);
        Assert.False(dialog.ConfirmCommand.CanExecute(null));

        dialog.LabelNamesText += Environment.NewLine + "bike";

        Assert.True(dialog.HasChanges);
        Assert.True(dialog.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void ProjectSetupDialogViewModel_RequiresImagesAndLabelsBeforeConfirm()
    {
        var dialog = new ProjectSetupDialogViewModel(
            "sample",
            ProjectKind.ObjectDetection,
            () => [@"C:\images\a.png"],
            () => ["car", "person"],
            _ => Task.FromResult(true),
            () => Task.FromResult(false),
            _ => { },
            isCancelable: false);

        Assert.False(dialog.IsCancelable);
        Assert.False(dialog.ConfirmCommand.CanExecute(null));

        dialog.BrowseImagesCommand.Execute(null);
        Assert.False(dialog.ConfirmCommand.CanExecute(null));

        dialog.LoadLabelsFromFileCommand.Execute(null);
        Assert.True(dialog.ConfirmCommand.CanExecute(null));
        Assert.Equal(["car", "person"], dialog.ParsedLabelsPreview);
    }

    [Fact]
    public void ProjectSetupDialogViewModel_CanOpenExistingProjectFromStartup()
    {
        var opened = false;
        var closed = false;
        var dialog = new ProjectSetupDialogViewModel(
            "sample",
            ProjectKind.ObjectDetection,
            () => null,
            () => null,
            _ => Task.FromResult(false),
            () =>
            {
                opened = true;
                return Task.FromResult(true);
            },
            _ => closed = true,
            isCancelable: false);

        dialog.OpenExistingProjectCommand.Execute(null);

        Assert.True(opened);
        Assert.True(closed);
    }

    [Fact]
    public void ImportAnnotationsDialogViewModel_ChangingFormat_ClearsSelectedPaths()
    {
        var dialog = new ImportAnnotationsDialogViewModel(
            [AnnotationFormat.Coco, AnnotationFormat.Yolo],
            AnnotationFormat.Coco,
            format => format == AnnotationFormat.Coco ? [@"C:\tmp\dataset.json"] : [@"C:\tmp\labels.txt", @"C:\tmp\image1.txt"],
            _ => true,
            _ => { });

        dialog.BrowseSourcesCommand.Execute(null);
        Assert.Single(dialog.SelectedPaths);
        Assert.True(dialog.ConfirmCommand.CanExecute(null));

        dialog.SelectedFormat = AnnotationFormat.Yolo;

        Assert.Empty(dialog.SelectedPaths);
        Assert.False(dialog.ConfirmCommand.CanExecute(null));
        Assert.Equal("No source selected.", dialog.SelectedPathsSummary);
    }

    [Fact]
    public void ExportAnnotationsDialogViewModel_DefaultExtensionHint_TracksSelectedFormat()
    {
        var dialog = new ExportAnnotationsDialogViewModel(
            [AnnotationFormat.Csv, AnnotationFormat.Yolo],
            AnnotationFormat.Csv,
            string.Empty,
            _ => @"C:\tmp\annotations.csv",
            _ => true,
            _ => { });

        Assert.Contains(".csv", dialog.DefaultExtensionHint, StringComparison.Ordinal);

        dialog.SelectedFormat = AnnotationFormat.Yolo;

        Assert.Contains(".zip", dialog.DefaultExtensionHint, StringComparison.Ordinal);

        dialog.SelectedFormat = AnnotationFormat.YoloImageTxt;

        Assert.Contains(".zip", dialog.DefaultExtensionHint, StringComparison.Ordinal);
    }

    [Fact]
    public void InferenceSetupDialogViewModel_RequiresModelPathBeforeConfirm()
    {
        var dialog = new InferenceSetupDialogViewModel(
            ["rect-detection", "pose-estimation"],
            "rect-detection",
            string.Empty,
            () => @"C:\models\detector.onnx",
            _ => true,
            _ => { });

        Assert.False(dialog.ConfirmCommand.CanExecute(null));

        dialog.BrowseModelCommand.Execute(null);

        Assert.True(dialog.ConfirmCommand.CanExecute(null));
    }
}
