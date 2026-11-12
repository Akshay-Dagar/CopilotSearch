using System.ComponentModel;
using System.Text.RegularExpressions;
using CopilotSearch.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CopilotSearch.Tools;

/// <summary>search_files and get_file_content.</summary>
public sealed partial class FilesTools(
    IFilesConnector files,
    IOptions<ToolOptions> options,
    ILogger<FilesTools> logger) : ToolBase(options, logger)
{
    private const int DefaultFileChars = 8000;
    private const string FilesPrefix = Sources.Files + ":";

    [GeneratedRegex(@"^\d+(-\d+)?$")]
    private static partial Regex PageRangePattern();

    [Description("Searches SharePoint and OneDrive files by keywords and metadata. Returns snippets with drive_id/item_id metadata; use get_file_content to read a file's text. Resolve people via resolve_person first for author/modified_by.")]
    public Task<object> SearchFiles(
        [Description("Keywords to match in file name or content.")] string? query = null,
        [Description("File extensions without the dot, e.g. ['docx','xlsx','pptx','pdf'].")] string[]? file_types = null,
        [Description("Where to search: 'sharepoint', 'onedrive' or 'any'. Default 'any'.")] string? location = null,
        [Description("SharePoint site name.")] string? site = null,
        [Description("Author (creator) name or email. Resolve the person via resolve_person first.")] string? author = null,
        [Description("Name or email of the person who last modified the file.")] string? modified_by = null,
        [Description("Only files last modified at or after this ISO-8601 UTC date-time, e.g. 2026-11-05T00:00:00Z.")] string? start = null,
        [Description("Only files last modified at or before this ISO-8601 UTC date-time.")] string? end = null,
        [Description("Maximum results. Default 10, maximum 25.")] int? top = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(ToolNames.SearchFiles, cancellationToken, async ct =>
        {
            var locationValue = ParseEnum(location, "location", "any", "sharepoint", "onedrive", "any");
            var range = ParseRange(start, end);
            var types = file_types?
                .Select(t => t?.Trim().TrimStart('.').ToLowerInvariant() ?? "")
                .Where(t => t.Length > 0)
                .Distinct()
                .ToList();

            var request = new FileSearchRequest
            {
                Query = Blank(query),
                FileTypes = types is { Count: > 0 } ? types : null,
                Location = locationValue,
                Site = Blank(site),
                Author = Blank(author),
                ModifiedBy = Blank(modified_by),
                Range = range,
                Top = ClampTop(top),
            };

            var page = await files.SearchAsync(request, ct);
            var mapper = new ResultMapper(Options.MaxSnippetChars);
            var items = page.Items.Select(h => mapper.File(h.Item, h.Score)).ToList();

            return new SearchEnvelope
            {
                Results = items,
                Returned = items.Count,
                TotalEstimate = Math.Max(page.Total, items.Count),
                Truncated = page.Total > items.Count,
                QueryUsed = DescribeQuery(("query", request.Query),
                    ("file_types", request.FileTypes is null ? null : string.Join(',', request.FileTypes)),
                    ("location", locationValue), ("site", request.Site), ("author", request.Author),
                    ("modified_by", request.ModifiedBy), ("start", range.Start), ("end", range.End)),
            };
        });

    [Description("Reads the extracted text of one file, truncated to max_chars. Use drive_id and item_id from a search_files result (metadata) or search_all result.")]
    public Task<object> GetFileContent(
        [Description("Drive id, from the search result metadata 'drive_id'.")] string drive_id,
        [Description("Item id, from the search result metadata 'item_id'.")] string item_id,
        [Description("Maximum characters of text to return. Default 8000.")] int? max_chars = null,
        [Description("Page range such as '1-3' for pdf/pptx. Validated but not yet applied: the full text is returned.")] string? page_range = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(ToolNames.GetFileContent, cancellationToken, async ct =>
        {
            if (string.IsNullOrWhiteSpace(drive_id)) throw new ToolArgumentException("'drive_id' is required.");
            if (string.IsNullOrWhiteSpace(item_id)) throw new ToolArgumentException("'item_id' is required.");
            if (Blank(page_range) is { } pr && !PageRangePattern().IsMatch(pr))
                throw new ToolArgumentException($"'page_range' must look like '2' or '1-3', got '{page_range}'.");

            var itemId = item_id.Trim();
            if (itemId.StartsWith(FilesPrefix, StringComparison.OrdinalIgnoreCase)) itemId = itemId[FilesPrefix.Length..];
            var limit = ClampChars(max_chars, DefaultFileChars);

            var content = await files.GetContentAsync(drive_id.Trim(), itemId, ct)
                ?? throw new ToolNotFoundException($"No file found with drive_id '{drive_id}' and item_id '{item_id}'.");

            var text = content.Text.Length <= limit ? content.Text : content.Text[..limit];
            return new FileContentResult
            {
                Id = $"{FilesPrefix}{content.ItemId}",
                Name = content.Name,
                FileType = content.FileType,
                Text = text,
                ExtractedChars = text.Length,
                TotalChars = content.Text.Length,
                Truncated = content.Text.Length > limit,
                WebUrl = content.WebUrl,
            };
        });
}
