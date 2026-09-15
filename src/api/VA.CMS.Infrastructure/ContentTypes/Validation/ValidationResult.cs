namespace VA.CMS.Infrastructure.ContentTypes.Validation;

/// <summary>
/// Result of validating a single field value.
/// </summary>
public sealed class ValidationResult
{
    public static ValidationResult Ok() => new(true, null);
    public static ValidationResult Fail(string error) => new(false, error);

    public bool IsValid { get; }
    public string? Error { get; }

    private ValidationResult(bool isValid, string? error)
    {
        IsValid = isValid;
        Error = error;
    }
}
