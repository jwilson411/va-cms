using System.Text.Json;
using VA.CMS.Infrastructure.ContentTypes.Validation;

namespace VA.CMS.Infrastructure.ContentTypes.CustomFields;

/// <summary>
/// Example custom field type: a geographic coordinate pair stored as a JSON
/// string <c>{"lat":38.9,"lng":-77.0}</c>.
///
/// Demonstrates FR-DEV-05: a developer-supplied <see cref="ICustomFieldType"/>
/// registered via <c>builder.Services.AddCustomFieldType&lt;GeoPointFieldType&gt;()</c>.
///
/// The matching React component is registered in the admin SPA:
/// <code>
///   registerCustomField('geo_point', GeoPointField);
/// </code>
/// </summary>
public sealed class GeoPointFieldType : ICustomFieldType
{
    /// <inheritdoc/>
    public string TypeName => "geo_point";

    /// <inheritdoc/>
    /// <remarks>Stored as a JSON string: <c>{"lat":38.9,"lng":-77.0}</c>.</remarks>
    public Type StorageType => typeof(string);

    /// <inheritdoc/>
    /// <remarks>
    /// Accepts either:
    /// <list type="bullet">
    ///   <item><see langword="null"/> or empty string — valid (optional field semantics).</item>
    ///   <item>A JSON object with numeric <c>lat</c> (-90 to 90) and <c>lng</c> (-180 to 180).</item>
    /// </list>
    /// </remarks>
    public ValidationResult Validate(object? value)
    {
        if (value is null)
            return ValidationResult.Ok();

        var raw = value as string ?? value.ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return ValidationResult.Ok();

        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;

            if (!root.TryGetProperty("lat", out var latEl) ||
                !root.TryGetProperty("lng", out var lngEl))
            {
                return ValidationResult.Fail("GeoPoint value must contain both 'lat' and 'lng' properties.");
            }

            if (!latEl.TryGetDouble(out var lat) || lat < -90 || lat > 90)
                return ValidationResult.Fail("GeoPoint 'lat' must be a number between -90 and 90.");

            if (!lngEl.TryGetDouble(out var lng) || lng < -180 || lng > 180)
                return ValidationResult.Fail("GeoPoint 'lng' must be a number between -180 and 180.");

            return ValidationResult.Ok();
        }
        catch (JsonException)
        {
            return ValidationResult.Fail("GeoPoint value must be a valid JSON object.");
        }
    }
}
