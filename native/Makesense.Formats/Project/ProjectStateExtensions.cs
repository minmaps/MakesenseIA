using Makesense.Formats.Contracts;

namespace Makesense.Formats.Project;

public static class ProjectStateExtensions
{
    public static ProjectState SetActiveImage(this ProjectState state, string? imageId)
    {
        return state with { ActiveImageId = imageId };
    }

    public static ProjectState ReplaceImage(this ProjectState state, ImageRecord image)
    {
        var images = state.Images
            .Select(existing => existing.Id == image.Id ? image : existing)
            .ToArray();

        return state with { Images = images };
    }

    public static ProjectState ReplaceImages(this ProjectState state, IReadOnlyList<ImageRecord> images)
    {
        return state with { Images = images.ToArray() };
    }

    public static ProjectState ReplaceLabels(this ProjectState state, IReadOnlyList<LabelClass> labels)
    {
        return state with { Labels = labels.ToArray() };
    }

    public static ImageRecord? GetActiveImage(this ProjectState state)
    {
        return state.Images.FirstOrDefault(image => image.Id == state.ActiveImageId);
    }
}
