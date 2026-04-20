using System.Text.Json;
using System.Text.Json.Serialization;
using Makesense.Formats.Contracts;

namespace Makesense.Formats.Serialization;

public static class ProjectStateSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task SaveAsync(ProjectState state, string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, state, Options, cancellationToken);
    }

    public static async Task<ProjectState?> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<ProjectState>(stream, Options, cancellationToken);
    }

    public static string ToJson(ProjectState state)
    {
        return JsonSerializer.Serialize(state, Options);
    }
}
