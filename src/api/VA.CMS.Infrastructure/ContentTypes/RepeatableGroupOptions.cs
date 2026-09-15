namespace VA.CMS.Infrastructure.ContentTypes;

/// <summary>
/// Options for a <see cref="FieldType.RepeatableGroup"/> field.
/// Provides the ordered list of nested field definitions that each row
/// in the group must satisfy.
/// </summary>
public sealed class RepeatableGroupOptions
{
    /// <summary>Nested field definitions for a single row of the repeatable group.</summary>
    public IReadOnlyList<FieldDefinition> Fields { get; }

    public RepeatableGroupOptions(IReadOnlyList<FieldDefinition> fields)
    {
        if (fields is null || fields.Count == 0)
            throw new ArgumentException(
                "RepeatableGroup must declare at least one nested field.", nameof(fields));

        Fields = fields;
    }
}
