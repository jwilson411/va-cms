namespace VA.CMS.Infrastructure.ContentTypes.Validation;

/// <summary>
/// Validates a single field value against its <see cref="FieldDefinition"/>.
/// </summary>
public interface IFieldValidator
{
    /// <summary>
    /// Validates <paramref name="value"/> against <paramref name="field"/>.
    /// Returns <see cref="ValidationResult.Ok()"/> when valid.
    /// </summary>
    ValidationResult Validate(FieldDefinition field, object? value);
}
