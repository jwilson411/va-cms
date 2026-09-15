using System.Text.Json;
using System.Text.RegularExpressions;

namespace VA.CMS.Infrastructure.ContentTypes.Validation;

/// <summary>
/// Default implementation of <see cref="IFieldValidator"/> for all 12 built-in
/// field types (FR-SCHEMA-02).
///
/// Validators for MediaReference, SingleRelation, and MultiRelation perform
/// ID-presence checks. Actual DB existence checks are done by
/// <see cref="IFieldReferenceResolver"/> at publish time.
/// </summary>
public sealed class FieldValidator : IFieldValidator
{
    // RFC 5321 / RFC 5322 simplified — covers all realistic addresses
    private static readonly Regex _emailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    public ValidationResult Validate(FieldDefinition field, object? value)
    {
        // Required check: null/blank fails for all types
        if (value is null || (value is string s && string.IsNullOrWhiteSpace(s)))
        {
            if (field.Required)
                return ValidationResult.Fail($"Field '{field.Name}' is required.");
            return ValidationResult.Ok();
        }

        return field.Type switch
        {
            FieldType.ShortText      => ValidateShortText(field, value),
            FieldType.LongText       => ValidateLongText(field, value),
            FieldType.RichText       => ValidateRichText(field, value),
            FieldType.Number         => ValidateNumber(field, value),
            FieldType.Boolean        => ValidateBoolean(field, value),
            FieldType.DateTime       => ValidateDateTime(field, value),
            FieldType.Url            => ValidateUrl(field, value),
            FieldType.Email          => ValidateEmail(field, value),
            FieldType.MediaReference => ValidateMediaReference(field, value),
            FieldType.SingleRelation => ValidateSingleRelation(field, value),
            FieldType.MultiRelation  => ValidateMultiRelation(field, value),
            FieldType.RepeatableGroup=> ValidateRepeatableGroup(field, value),
            FieldType.Json           => ValidateJson(field, value),
            _                        => ValidationResult.Fail(
                $"Unknown field type '{field.Type}' for field '{field.Name}'.")
        };
    }

    // ── ShortText ────────────────────────────────────────────────────────────

    private static ValidationResult ValidateShortText(FieldDefinition field, object value)
    {
        if (value is not string text)
            return ValidationResult.Fail(
                $"Field '{field.Name}' (ShortText) must be a string.");

        if (field.MaxLength.HasValue && text.Length > field.MaxLength.Value)
            return ValidationResult.Fail(
                $"Field '{field.Name}' exceeds maximum length of {field.MaxLength.Value} characters.");

        return ValidationResult.Ok();
    }

    // ── LongText ─────────────────────────────────────────────────────────────

    private static ValidationResult ValidateLongText(FieldDefinition field, object value)
    {
        if (value is not string text)
            return ValidationResult.Fail(
                $"Field '{field.Name}' (LongText) must be a string.");

        if (field.MaxLength.HasValue && text.Length > field.MaxLength.Value)
            return ValidationResult.Fail(
                $"Field '{field.Name}' exceeds maximum length of {field.MaxLength.Value} characters.");

        return ValidationResult.Ok();
    }

    // ── RichText (CommonMark Markdown stored, never raw HTML) ────────────────

    private static ValidationResult ValidateRichText(FieldDefinition field, object value)
    {
        if (value is not string)
            return ValidationResult.Fail(
                $"Field '{field.Name}' (RichText) must be a Markdown string.");

        // RichText has no length restriction at the validator layer — DB stores NVARCHAR(MAX).
        return ValidationResult.Ok();
    }

    // ── Number ───────────────────────────────────────────────────────────────

    private static ValidationResult ValidateNumber(FieldDefinition field, object value)
    {
        if (value is int or long or float or double or decimal)
            return ValidationResult.Ok();

        if (value is string s && double.TryParse(s,
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out _))
            return ValidationResult.Ok();

        return ValidationResult.Fail(
            $"Field '{field.Name}' (Number) must be a numeric value.");
    }

    // ── Boolean ──────────────────────────────────────────────────────────────

    private static ValidationResult ValidateBoolean(FieldDefinition field, object value)
    {
        if (value is bool)
            return ValidationResult.Ok();

        if (value is string s)
        {
            if (bool.TryParse(s, out _))
                return ValidationResult.Ok();
            if (s is "0" or "1")
                return ValidationResult.Ok();
        }

        return ValidationResult.Fail(
            $"Field '{field.Name}' (Boolean) must be true or false.");
    }

    // ── DateTime ─────────────────────────────────────────────────────────────

    private static ValidationResult ValidateDateTime(FieldDefinition field, object value)
    {
        if (value is System.DateTime or DateTimeOffset)
            return ValidationResult.Ok();

        if (value is string s && (
                System.DateTime.TryParse(s,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out _) ||
                DateTimeOffset.TryParse(s,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out _)))
            return ValidationResult.Ok();

        return ValidationResult.Fail(
            $"Field '{field.Name}' (DateTime) must be a valid ISO 8601 date/time.");
    }

    // ── URL ──────────────────────────────────────────────────────────────────

    private static ValidationResult ValidateUrl(FieldDefinition field, object value)
    {
        if (value is not string s)
            return ValidationResult.Fail($"Field '{field.Name}' (URL) must be a string.");

        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return ValidationResult.Ok();

        return ValidationResult.Fail(
            $"Field '{field.Name}' must be a valid absolute HTTP/HTTPS URL.");
    }

    // ── Email ────────────────────────────────────────────────────────────────

    private ValidationResult ValidateEmail(FieldDefinition field, object value)
    {
        if (value is not string s)
            return ValidationResult.Fail($"Field '{field.Name}' (Email) must be a string.");

        if (_emailRegex.IsMatch(s))
            return ValidationResult.Ok();

        return ValidationResult.Fail(
            $"Field '{field.Name}' must be a valid email address.");
    }

    // ── MediaReference ───────────────────────────────────────────────────────

    private static ValidationResult ValidateMediaReference(FieldDefinition field, object value)
    {
        // Expects a MediaAsset ID (long or parseable string).
        if (!TryGetLongId(value, out var id))
            return ValidationResult.Fail(
                $"Field '{field.Name}' (MediaReference) must be a positive integer MediaAsset ID.");

        if (id <= 0)
            return ValidationResult.Fail(
                $"Field '{field.Name}' (MediaReference) ID must be greater than zero.");

        return ValidationResult.Ok();
    }

    // ── SingleRelation ───────────────────────────────────────────────────────

    private static ValidationResult ValidateSingleRelation(FieldDefinition field, object value)
    {
        if (!TryGetLongId(value, out var id))
            return ValidationResult.Fail(
                $"Field '{field.Name}' (SingleRelation) must be a positive integer ContentEntry ID.");

        if (id <= 0)
            return ValidationResult.Fail(
                $"Field '{field.Name}' (SingleRelation) ID must be greater than zero.");

        return ValidationResult.Ok();
    }

    // ── MultiRelation ────────────────────────────────────────────────────────

    private static ValidationResult ValidateMultiRelation(FieldDefinition field, object value)
    {
        // Expects a non-empty array of ContentEntry IDs.
        IEnumerable<object>? ids = value switch
        {
            IEnumerable<long>   l => l.Cast<object>(),
            IEnumerable<int>    i => i.Cast<object>(),
            IEnumerable<object> o => o,
            _                    => null
        };

        if (ids is null)
        {
            // Try JSON array string
            if (value is string json)
            {
                try
                {
                    var arr = JsonSerializer.Deserialize<long[]>(json);
                    if (arr is null)
                        return ValidationResult.Fail(
                            $"Field '{field.Name}' (MultiRelation) must be an array of ContentEntry IDs.");
                    ids = arr.Cast<object>();
                }
                catch
                {
                    return ValidationResult.Fail(
                        $"Field '{field.Name}' (MultiRelation) must be a JSON array of ContentEntry IDs.");
                }
            }
            else
            {
                return ValidationResult.Fail(
                    $"Field '{field.Name}' (MultiRelation) must be an array of ContentEntry IDs.");
            }
        }

        foreach (var item in ids)
        {
            if (!TryGetLongId(item, out var id) || id <= 0)
                return ValidationResult.Fail(
                    $"Field '{field.Name}' (MultiRelation) contains invalid ID '{item}'. All IDs must be positive integers.");
        }

        return ValidationResult.Ok();
    }

    // ── RepeatableGroup ──────────────────────────────────────────────────────

    private static ValidationResult ValidateRepeatableGroup(FieldDefinition field, object value)
    {
        // RepeatableGroup must have nested field definitions in Options.
        // The value must be a JSON array of objects (one per row).
        // Validator ensures the value is parseable as a non-null JSON array;
        // nested field-level validation is handled by callers per row.

        if (field.Options is null)
            return ValidationResult.Fail(
                $"Field '{field.Name}' (RepeatableGroup) must declare nested field definitions in Options.");

        string? json = value switch
        {
            string s => s,
            _ => null
        };

        if (json is null)
        {
            try { json = JsonSerializer.Serialize(value); }
            catch
            {
                return ValidationResult.Fail(
                    $"Field '{field.Name}' (RepeatableGroup) value is not serializable.");
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return ValidationResult.Fail(
                    $"Field '{field.Name}' (RepeatableGroup) value must be a JSON array.");
        }
        catch (JsonException)
        {
            return ValidationResult.Fail(
                $"Field '{field.Name}' (RepeatableGroup) value is not valid JSON.");
        }

        return ValidationResult.Ok();
    }

    // ── JSON ─────────────────────────────────────────────────────────────────

    private static ValidationResult ValidateJson(FieldDefinition field, object value)
    {
        string? json = value switch
        {
            string s => s,
            _ => null
        };

        if (json is null)
            return ValidationResult.Fail(
                $"Field '{field.Name}' (JSON) must be a JSON string.");

        try
        {
            using var doc = JsonDocument.Parse(json);
            return ValidationResult.Ok();
        }
        catch (JsonException ex)
        {
            return ValidationResult.Fail(
                $"Field '{field.Name}' (JSON) contains malformed JSON: {ex.Message}");
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool TryGetLongId(object value, out long id)
    {
        id = 0;
        return value switch
        {
            long l   => (id = l) > long.MinValue,   // always sets
            int  i   => (id = i) >= int.MinValue,
            string s => long.TryParse(s, out id),
            _        => false
        };
    }
}
