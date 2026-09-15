namespace VA.CMS.Infrastructure.ContentTypes;

/// <summary>
/// Holds all registered custom field type plugins.
/// Retrieve via DI: inject <see cref="ICustomFieldTypeRegistry"/>.
/// </summary>
public interface ICustomFieldTypeRegistry
{
    /// <summary>Returns all registered custom field types (registration order).</summary>
    IReadOnlyList<ICustomFieldType> GetAll();

    /// <summary>
    /// Resolves a custom field type by its <paramref name="typeName"/>.
    /// Returns <c>null</c> if no custom type with that name is registered.
    /// </summary>
    ICustomFieldType? GetByName(string typeName);
}
