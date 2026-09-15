namespace VA.CMS.Infrastructure.ContentTypes;

/// <summary>
/// Describes a single field within a content type definition.
/// </summary>
public sealed class FieldDefinition
{
    /// <summary>Machine-readable name (e.g. "title", "publishDate").</summary>
    public string Name { get; }

    /// <summary>Human-readable label used in the admin form editor.</summary>
    public string Label { get; }

    /// <summary>The field type (one of the 12 built-in types).</summary>
    public FieldType Type { get; }

    /// <summary>Whether the field must be non-empty before publishing.</summary>
    public bool Required { get; }

    /// <summary>Maximum character length (for text-based field types).</summary>
    public int? MaxLength { get; }

    /// <summary>Arbitrary type-specific options serialized as an object (e.g. TaxonomyHandle).</summary>
    public object? Options { get; }

    public FieldDefinition(
        string name,
        FieldType type,
        bool required = false,
        int? maxLength = null,
        string? label = null,
        object? options = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Field name must not be blank.", nameof(name));

        Name = name;
        Type = type;
        Required = required;
        MaxLength = maxLength;
        Label = label ?? name;
        Options = options;
    }
}
