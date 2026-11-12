using System.Reflection;
using System.Text.Json;
using CopilotSearch.Contracts;

namespace CopilotSearch.Connectors;

/// <summary>Loads the embedded JSON fixtures once, on first use.</summary>
public sealed class FixtureStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly Lazy<FixtureData> _data = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public IReadOnlyList<UserProfile> Users => _data.Value.Users;
    public IReadOnlyList<GraphMessage> Messages => _data.Value.Messages;
    public IReadOnlyList<GraphChatMessage> ChatMessages => _data.Value.ChatMessages;
    public IReadOnlyList<GraphDriveItem> DriveItems => _data.Value.DriveItems;
    public IReadOnlyList<AiInteraction> Interactions => _data.Value.Interactions;
    public IReadOnlyList<GraphEvent> Events => _data.Value.Events;

    /// <summary>Extracted text keyed by "driveId/itemId".</summary>
    public IReadOnlyDictionary<string, string> FileTexts => _data.Value.FileTexts;

    internal static string FileKey(string driveId, string itemId) => $"{driveId}/{itemId}";

    private static FixtureData Load()
    {
        var files = ReadResource("files.json");
        var items = new List<GraphDriveItem>();
        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var element in files.RootElement.EnumerateArray())
        {
            var item = element.Deserialize<GraphDriveItem>(JsonOptions)!;
            items.Add(item);
            texts[FileKey(item.DriveId, item.Id)] = element.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
        }

        return new FixtureData(
            ReadList<UserProfile>("users.json"),
            ReadList<GraphMessage>("messages.json"),
            ReadList<GraphChatMessage>("teams.json"),
            items,
            ReadList<AiInteraction>("copilot.json"),
            ReadList<GraphEvent>("events.json"),
            texts);
    }

    private static List<T> ReadList<T>(string name)
    {
        using var stream = OpenResource(name);
        return JsonSerializer.Deserialize<List<T>>(stream, JsonOptions)
               ?? throw new InvalidOperationException($"Fixture {name} is empty.");
    }

    private static JsonDocument ReadResource(string name)
    {
        using var stream = OpenResource(name);
        return JsonDocument.Parse(stream);
    }

    private static Stream OpenResource(string name) =>
        typeof(FixtureStore).Assembly.GetManifestResourceStream($"Fixtures.{name}")
        ?? throw new InvalidOperationException($"Embedded fixture Fixtures.{name} not found.");

    private sealed record FixtureData(
        List<UserProfile> Users,
        List<GraphMessage> Messages,
        List<GraphChatMessage> ChatMessages,
        List<GraphDriveItem> DriveItems,
        List<AiInteraction> Interactions,
        List<GraphEvent> Events,
        Dictionary<string, string> FileTexts);
}
