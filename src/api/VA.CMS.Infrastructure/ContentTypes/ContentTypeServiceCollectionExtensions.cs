using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VA.CMS.Infrastructure.ContentTypes.Validation;

namespace VA.CMS.Infrastructure.ContentTypes;

/// <summary>
/// DI extension methods for registering content types.
/// </summary>
public static class ContentTypeServiceCollectionExtensions
{
    /// <summary>
    /// Registers a content type definition with the DI container.
    /// The type is discoverable via <see cref="IFieldTypeRegistry"/>.
    ///
    /// Call this once per type in Program.cs or a module:
    /// <code>
    /// builder.Services.AddContentType&lt;StandardPageTypeDefinition&gt;();
    /// </code>
    /// </summary>
    /// <typeparam name="T">
    /// A concrete <see cref="ContentTypeDefinitionBase"/> subclass with a
    /// public parameterless constructor.
    /// </typeparam>
    public static IServiceCollection AddContentType<T>(this IServiceCollection services)
        where T : ContentTypeDefinitionBase, new()
    {
        // Register the concrete type as its abstract base so the registry
        // can collect all of them via IEnumerable<ContentTypeDefinitionBase>.
        services.AddSingleton<ContentTypeDefinitionBase, T>();

        // Ensure the registry itself is registered exactly once.
        services.TryAddSingleton<IFieldTypeRegistry>(sp =>
            new FieldTypeRegistry(sp.GetServices<ContentTypeDefinitionBase>()));

        // Register the field validator (singleton — stateless).
        services.TryAddSingleton<IFieldValidator, FieldValidator>();

        return services;
    }

    /// <summary>
    /// Registers the <see cref="IFieldTypeRegistry"/> with no built-in content types.
    /// Useful when no content types have been configured yet (e.g. test hosts).
    /// </summary>
    public static IServiceCollection AddContentTypeRegistry(this IServiceCollection services)
    {
        services.TryAddSingleton<IFieldTypeRegistry>(sp =>
            new FieldTypeRegistry(sp.GetServices<ContentTypeDefinitionBase>()));

        // Register the field validator (singleton — stateless).
        services.TryAddSingleton<IFieldValidator, FieldValidator>();

        return services;
    }
}
