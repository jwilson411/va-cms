using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using VA.CMS.Infrastructure.ContentTypes;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Services;

/// <summary>
/// Keeps MediaUsage in step with what an entry's fields actually reference (#158).
///
/// Serve's anonymous audience is "assets a Published entry references", which is
/// answered from MediaUsage. Nothing populated that table automatically before —
/// the SPA never called /sync-media-usage — so it is derived server-side on every
/// version save from the entry's MediaReference fields and from any
/// /api/v1/media/serve/{id} URL embedded in string fields (Markdown bodies).
/// </summary>
public interface IMediaUsageSyncService
{
    Task SyncAsync(long entryId, string? contentTypeName, string? fieldsJson, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed partial class MediaUsageSyncService(
    IMediaExtendedRepository usage,
    IFieldTypeRegistry registry,
    ILogger<MediaUsageSyncService> logger) : IMediaUsageSyncService
{
    [GeneratedRegex(@"/api/v1/media/serve/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ServeUrl();

    public async Task SyncAsync(long entryId, string? contentTypeName, string? fieldsJson, CancellationToken ct = default)
    {
        var references = Extract(contentTypeName, fieldsJson, registry);

        // The version is already saved; an index failure is logged, never surfaced as a 500.
        try
        {
            await usage.DeleteUsageForEntryAsync(entryId);
            foreach (var (assetId, fieldName) in references)
                await usage.UpsertUsageAsync(assetId, entryId, fieldName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Media usage sync failed for entry {EntryId}; public serve gating may lag until the next save.", entryId);
        }
    }

    /// <summary>Asset references in a FieldsJson blob: (assetId, fieldName), de-duplicated.</summary>
    public static IReadOnlyList<(long AssetId, string FieldName)> Extract(
        string? contentTypeName, string? fieldsJson, IFieldTypeRegistry registry)
    {
        if (string.IsNullOrWhiteSpace(fieldsJson))
            return Array.Empty<(long, string)>();

        JsonObject? fields;
        try { fields = JsonNode.Parse(fieldsJson) as JsonObject; }
        catch (JsonException) { return Array.Empty<(long, string)>(); }
        if (fields is null)
            return Array.Empty<(long, string)>();

        var found = new HashSet<(long, string)>();

        var definition = contentTypeName is null ? null : registry.GetByName(contentTypeName);
        var mediaFields = definition?.Fields
            .Where(f => f.Type == FieldType.MediaReference)
            .Select(f => f.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, node) in fields)
        {
            if (node is null) continue;

            if (mediaFields.Contains(name) && TryReadAssetId(node, out var id))
                found.Add((id, name));

            foreach (var text in StringValues(node))
                foreach (Match m in ServeUrl().Matches(text))
                    if (long.TryParse(m.Groups[1].Value, out var urlId))
                        found.Add((urlId, name));
        }

        return found.ToList();
    }

    private static bool TryReadAssetId(JsonNode node, out long id)
    {
        id = 0;
        if (node is JsonValue value)
        {
            if (value.TryGetValue<long>(out id)) return id > 0;
            if (value.TryGetValue<string>(out var s) && long.TryParse(s, out id)) return id > 0;
            return false;
        }
        // Expanded shape { id, storageUrl, … } may be echoed back by the editor.
        if (node is JsonObject obj && obj["id"] is JsonValue idValue && idValue.TryGetValue<long>(out id))
            return id > 0;
        return false;
    }

    private static IEnumerable<string> StringValues(JsonNode node)
    {
        switch (node)
        {
            case JsonValue v when v.TryGetValue<string>(out var s):
                yield return s;
                break;
            case JsonArray arr:
                foreach (var item in arr)
                    if (item is not null)
                        foreach (var s in StringValues(item)) yield return s;
                break;
            case JsonObject obj:
                foreach (var (_, child) in obj)
                    if (child is not null)
                        foreach (var s in StringValues(child)) yield return s;
                break;
        }
    }
}
