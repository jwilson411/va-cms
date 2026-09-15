using VA.CMS.Infrastructure.ContentTypes.Validation;

namespace VA.CMS.Infrastructure.ContentTypes;

/// <summary>
/// Defines a custom field type plugin that can be registered at startup
/// and rendered in the admin form editor via a paired React component.
///
/// FR-SCHEMA-04 / FR-DEV-05 / Issue #27.
///
/// Implementation:
/// <code>
/// public class GeoPointFieldType : ICustomFieldType
/// {
///     public string TypeName => "geo_point";
///     public Type StorageType => typeof(string); // stored as JSON string
///
///     public ValidationResult Validate(object? value)
///     {
///         // validate JSON has { lat, lng } ...
///         return ValidationResult.Ok();
///     }
/// }
///
/// // Register in Program.cs:
/// builder.Services.AddCustomFieldType&lt;GeoPointFieldType&gt;();
/// </code>
/// </summary>
public interface ICustomFieldType
{
    /// <summary>
    /// Unique, machine-readable type name used as the discriminator in field
    /// definitions and in the admin registry sent to the React SPA.
    /// Must be unique across all built-in and custom types (snake_case recommended).
    /// </summary>
    string TypeName { get; }

    /// <summary>
    /// The .NET storage type used when persisting the field value.
    /// Typical choices: <see cref="string"/>, <see cref="int"/>, <see cref="double"/>.
    /// </summary>
    Type StorageType { get; }

    /// <summary>
    /// Validates the raw <paramref name="value"/> supplied by the form editor.
    /// Return <see cref="ValidationResult.Ok()"/> when valid, or
    /// <see cref="ValidationResult.Fail(string)"/> with a human-readable message.
    /// </summary>
    ValidationResult Validate(object? value);
}
