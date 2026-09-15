namespace VA.CMS.Infrastructure.ContentTypes;

/// <summary>
/// Injectable service that resolves all registered content type definitions.
/// Register types with <c>builder.Services.AddContentType&lt;T&gt;()</c>;
/// they are automatically discoverable here.
/// </summary>
public interface IFieldTypeRegistry
{
    /// <summary>Returns all registered content type definitions (in registration order).</summary>
    IReadOnlyList<ContentTypeDefinitionBase> GetAll();

    /// <summary>
    /// Resolves a content type definition by its machine-readable <paramref name="name"/>.
    /// Returns null if no type with that name is registered.
    /// </summary>
    ContentTypeDefinitionBase? GetByName(string name);

    /// <summary>Returns the names of all 12 built-in field types.</summary>
    IReadOnlyList<string> GetBuiltInFieldTypeNames();
}
