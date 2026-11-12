using System.Collections.Concurrent;
using CopilotSearch.Contracts;

namespace CopilotSearch.Orchestration;

/// <summary>Per-request store of every result item returned by tools. Citations resolve against it.</summary>
public sealed class ResultRegistry
{
    private readonly ConcurrentDictionary<string, ResultItem> _items = new(StringComparer.Ordinal);

    public int Count => _items.Count;

    public void Add(ResultItem item)
    {
        // Keep the best-scored copy when the same item is returned by several tools.
        _items.AddOrUpdate(item.Id, item, (_, existing) => item.Score > existing.Score ? item : existing);
    }

    public bool TryGet(string id, out ResultItem item) => _items.TryGetValue(id, out item!);

    public IReadOnlyList<ResultItem> All() => [.. _items.Values.OrderByDescending(i => i.Score)];
}
