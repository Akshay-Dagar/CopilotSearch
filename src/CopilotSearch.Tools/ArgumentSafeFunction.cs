using System.Text.Json;
using System.Text.Json.Nodes;
using CopilotSearch.Contracts;
using Microsoft.Extensions.AI;

namespace CopilotSearch.Tools;

/// <summary>
/// Wraps a tool function so that argument binding failures (missing or wrongly typed parameters) come back as an
/// invalid_argument <see cref="ErrorEnvelope"/> naming the parameter, and enriches the input schema with enums.
/// </summary>
internal sealed class ArgumentSafeFunction : DelegatingAIFunction
{
    private static readonly Dictionary<string, string[]> Enums = new()
    {
        ["folder"] = ["inbox", "sent", "drafts", "archive", "all"],
        ["importance"] = ["low", "normal", "high"],
        ["sort"] = ["relevance", "date_desc", "date_asc"],
        ["scope"] = ["chat", "channel", "any"],
        ["location"] = ["sharepoint", "onedrive", "any"],
        ["app"] = ["bizchat", "teams", "word", "excel", "powerpoint", "outlook", "any"],
    };

    private readonly JsonElement _schema;

    public ArgumentSafeFunction(AIFunction inner) : base(inner) => _schema = AddEnums(inner.JsonSchema);

    public override JsonElement JsonSchema => _schema;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        if (FindProblem(arguments) is { } problem)
            return ErrorEnvelope.Create(ToolErrorCodes.InvalidArgument, problem);

        try
        {
            return await base.InvokeCoreAsync(arguments, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ErrorEnvelope.Create(ToolErrorCodes.InvalidArgument,
                $"Invalid arguments for '{Name}': {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    private string? FindProblem(AIFunctionArguments arguments)
    {
        if (!_schema.TryGetProperty("properties", out var properties) || properties.ValueKind != JsonValueKind.Object)
            return null;

        if (_schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
        {
            foreach (var name in required.EnumerateArray().Select(r => r.GetString()!))
            {
                if (!arguments.TryGetValue(name, out var value) || value is null || IsJsonNull(value))
                    return $"Missing required parameter '{name}'.";
            }
        }

        foreach (var property in properties.EnumerateObject())
        {
            if (!arguments.TryGetValue(property.Name, out var value) || value is null || IsJsonNull(value)) continue;
            var expected = ExpectedKinds(property.Value);
            if (expected.Count == 0) continue;
            var actual = ToElement(value);
            if (!expected.Any(k => Matches(k, actual)))
                return $"Parameter '{property.Name}' must be of type {string.Join(" or ", expected)}, got {Describe(actual)}.";
        }
        return null;
    }

    private static bool IsJsonNull(object value) => value is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined };

    private static JsonElement ToElement(object value) =>
        value is JsonElement e ? e : JsonSerializer.SerializeToElement(value, value.GetType());

    private static List<string> ExpectedKinds(JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var type)) return [];
        var kinds = type.ValueKind == JsonValueKind.Array
            ? type.EnumerateArray().Select(t => t.GetString()!)
            : [type.GetString()!];
        return kinds.Where(k => k != "null").ToList();
    }

    private static bool Matches(string kind, JsonElement actual) => kind switch
    {
        "string" => actual.ValueKind == JsonValueKind.String,
        "integer" => actual.ValueKind == JsonValueKind.Number && actual.TryGetInt64(out _)
            || actual.ValueKind == JsonValueKind.String && long.TryParse(actual.GetString(), out _),
        "number" => actual.ValueKind == JsonValueKind.Number
            || actual.ValueKind == JsonValueKind.String && double.TryParse(actual.GetString(), out _),
        "boolean" => actual.ValueKind is JsonValueKind.True or JsonValueKind.False
            || actual.ValueKind == JsonValueKind.String && bool.TryParse(actual.GetString(), out _),
        "array" => actual.ValueKind == JsonValueKind.Array,
        "object" => actual.ValueKind == JsonValueKind.Object,
        _ => true,
    };

    private static string Describe(JsonElement actual) => actual.ValueKind switch
    {
        JsonValueKind.String => $"string \"{actual.GetString()}\"",
        JsonValueKind.Number => $"number {actual.GetRawText()}",
        JsonValueKind.True or JsonValueKind.False => "boolean",
        JsonValueKind.Array => "array",
        JsonValueKind.Object => "object",
        _ => actual.ValueKind.ToString().ToLowerInvariant(),
    };

    private static JsonElement AddEnums(JsonElement schema)
    {
        if (JsonNode.Parse(schema.GetRawText()) is not JsonObject root || root["properties"] is not JsonObject properties)
            return schema;

        foreach (var (name, values) in Enums)
        {
            if (properties[name] is not JsonObject property) continue;
            var allowed = new JsonArray(values.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
            allowed.Add(null);
            property["enum"] = allowed;
        }
        return JsonSerializer.SerializeToElement(root);
    }
}
