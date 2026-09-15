namespace VA.CMS.Infrastructure.ContentTypes;

/// <summary>
/// Default implementation of <see cref="ICustomFieldTypeRegistry"/>.
/// Populated from DI via the <c>IEnumerable&lt;ICustomFieldType&gt;</c> collection
/// registered by <c>builder.Services.AddCustomFieldType&lt;T&gt;()</c>.
/// </summary>
internal sealed class CustomFieldTypeRegistry : ICustomFieldTypeRegistry
{
    private readonly IReadOnlyList<ICustomFieldType> _types;
    private readonly Dictionary<string, ICustomFieldType> _byName;

    public CustomFieldTypeRegistry(IEnumerable<ICustomFieldType> types)
    {
        _types = types.ToList().AsReadOnly();
        _byName = _types.ToDictionary(
            t => t.TypeName,
            t => t,
            StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<ICustomFieldType> GetAll() => _types;

    public ICustomFieldType? GetByName(string typeName) =>
        _byName.TryGetValue(typeName, out var t) ? t : null;
}
