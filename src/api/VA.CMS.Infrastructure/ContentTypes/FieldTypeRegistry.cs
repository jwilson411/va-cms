namespace VA.CMS.Infrastructure.ContentTypes;

/// <summary>
/// Default implementation of <see cref="IFieldTypeRegistry"/>.
/// Holds the list of content type definitions registered at startup via
/// <c>builder.Services.AddContentType&lt;T&gt;()</c>.
/// </summary>
internal sealed class FieldTypeRegistry : IFieldTypeRegistry
{
    // The 13 built-in field types required by FR-SCHEMA-01 (epic scope lists 13)
    private static readonly IReadOnlyList<string> _builtInFieldTypeNames =
    [
        nameof(FieldType.ShortText),
        nameof(FieldType.LongText),
        nameof(FieldType.RichText),
        nameof(FieldType.Number),
        nameof(FieldType.Boolean),
        nameof(FieldType.DateTime),
        nameof(FieldType.Url),
        nameof(FieldType.Email),
        nameof(FieldType.MediaReference),
        nameof(FieldType.SingleRelation),
        nameof(FieldType.MultiRelation),
        nameof(FieldType.RepeatableGroup),
        nameof(FieldType.Json),
    ];

    private readonly IReadOnlyList<ContentTypeDefinitionBase> _types;

    public FieldTypeRegistry(IEnumerable<ContentTypeDefinitionBase> types)
    {
        _types = types.ToList().AsReadOnly();
    }

    public IReadOnlyList<ContentTypeDefinitionBase> GetAll() => _types;

    public ContentTypeDefinitionBase? GetByName(string name) =>
        _types.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<string> GetBuiltInFieldTypeNames() => _builtInFieldTypeNames;
}
