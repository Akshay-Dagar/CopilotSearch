using System.Text.Json.Nodes;

namespace CopilotSearch.Orchestration;

/// <summary>Caps the serialized size of a tool result before it goes back to the LLM.</summary>
public static class ToolOutputTruncator
{
    public static JsonNode Truncate(JsonNode node, int maxChars)
    {
        if (maxChars <= 0 || node.ToJsonString().Length <= maxChars)
            return node;

        if (node is JsonObject obj && obj["results"] is JsonArray results)
        {
            while (results.Count > 0 && obj.ToJsonString().Length > maxChars)
                results.RemoveAt(results.Count - 1);
            obj["truncated"] = true;
            obj["returned"] = results.Count;
            AddWarning(obj, "Output was truncated to fit the size limit; narrow the query or filters for more results.");
            return obj;
        }

        var text = node.ToJsonString();
        var keep = Math.Max(0, maxChars - 200);
        return new JsonObject
        {
            ["truncated"] = true,
            ["content"] = text[..Math.Min(keep, text.Length)] + " …[truncated]",
        };
    }

    private static void AddWarning(JsonObject obj, string warning)
    {
        if (obj["warnings"] is not JsonArray warnings)
            obj["warnings"] = warnings = new JsonArray();
        warnings.Add(warning);
    }
}
