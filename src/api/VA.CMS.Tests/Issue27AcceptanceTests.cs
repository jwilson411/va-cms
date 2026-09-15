using Microsoft.Extensions.DependencyInjection;
using VA.CMS.Infrastructure.ContentTypes;
using VA.CMS.Infrastructure.ContentTypes.CustomFields;
using VA.CMS.Infrastructure.ContentTypes.Validation;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #27:
/// Implement custom field type plugin registration (FR-SCHEMA-04 / FR-DEV-05).
///
/// AC:
/// - <see cref="ICustomFieldType"/> interface is publicly exported.
/// - <c>builder.Services.AddCustomFieldType&lt;T&gt;()</c> registers a new type.
/// - <see cref="ICustomFieldTypeRegistry"/> is injectable and returns registered types.
/// - <see cref="GeoPointFieldType"/> is included as a reference implementation.
/// </summary>
public class Issue27AcceptanceTests
{
    // ── ICustomFieldTypeRegistry unit tests ────────────────────────────────────

    private static ICustomFieldTypeRegistry BuildRegistryWithGeoPoint()
    {
        var services = new ServiceCollection();
        services.AddCustomFieldType<GeoPointFieldType>();
        return services.BuildServiceProvider().GetRequiredService<ICustomFieldTypeRegistry>();
    }

    [Fact]
    public void AddCustomFieldType_Registers_ICustomFieldTypeRegistry()
    {
        var services = new ServiceCollection();
        services.AddCustomFieldType<GeoPointFieldType>();
        var provider = services.BuildServiceProvider();

        var registry = provider.GetService<ICustomFieldTypeRegistry>();
        Assert.NotNull(registry);
    }

    [Fact]
    public void Registry_GetAll_Returns_Registered_CustomFieldType()
    {
        var registry = BuildRegistryWithGeoPoint();
        var types = registry.GetAll();

        Assert.NotEmpty(types);
        Assert.Contains(types, t => t.TypeName == "geo_point");
    }

    [Fact]
    public void Registry_GetByName_Returns_GeoPoint()
    {
        var registry = BuildRegistryWithGeoPoint();
        var type = registry.GetByName("geo_point");

        Assert.NotNull(type);
        Assert.IsType<GeoPointFieldType>(type);
    }

    [Fact]
    public void Registry_GetByName_Is_Case_Insensitive()
    {
        var registry = BuildRegistryWithGeoPoint();
        Assert.NotNull(registry.GetByName("GEO_POINT"));
        Assert.NotNull(registry.GetByName("Geo_Point"));
    }

    [Fact]
    public void Registry_GetByName_Returns_Null_For_Unknown_Type()
    {
        var registry = BuildRegistryWithGeoPoint();
        Assert.Null(registry.GetByName("no_such_type"));
    }

    [Fact]
    public void Registry_Empty_When_No_CustomTypes_Registered()
    {
        var services = new ServiceCollection();
        services.AddContentTypeRegistry();
        var registry = services.BuildServiceProvider().GetRequiredService<ICustomFieldTypeRegistry>();

        Assert.Empty(registry.GetAll());
    }

    // ── GeoPointFieldType unit tests ───────────────────────────────────────────

    private static GeoPointFieldType GeoPoint() => new();

    [Fact]
    public void GeoPoint_TypeName_Is_geo_point()
    {
        Assert.Equal("geo_point", GeoPoint().TypeName);
    }

    [Fact]
    public void GeoPoint_StorageType_Is_String()
    {
        Assert.Equal(typeof(string), GeoPoint().StorageType);
    }

    [Fact]
    public void GeoPoint_Validate_Null_Is_Valid()
    {
        var result = GeoPoint().Validate(null);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GeoPoint_Validate_Empty_String_Is_Valid()
    {
        var result = GeoPoint().Validate("");
        Assert.True(result.IsValid);
    }

    [Fact]
    public void GeoPoint_Validate_Valid_Coordinates_Pass()
    {
        var json = "{\"lat\":38.9,\"lng\":-77.0}";
        var result = GeoPoint().Validate(json);
        Assert.True(result.IsValid, result.Error);
    }

    [Fact]
    public void GeoPoint_Validate_Missing_Lat_Fails()
    {
        var json = "{\"lng\":-77.0}";
        var result = GeoPoint().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains("lat", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeoPoint_Validate_Missing_Lng_Fails()
    {
        var json = "{\"lat\":38.9}";
        var result = GeoPoint().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains("lng", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeoPoint_Validate_Lat_Out_Of_Range_Fails()
    {
        var json = "{\"lat\":91.0,\"lng\":0.0}";
        var result = GeoPoint().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains("lat", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeoPoint_Validate_Lng_Out_Of_Range_Fails()
    {
        var json = "{\"lat\":0.0,\"lng\":200.0}";
        var result = GeoPoint().Validate(json);
        Assert.False(result.IsValid);
        Assert.Contains("lng", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeoPoint_Validate_Invalid_Json_Fails()
    {
        var result = GeoPoint().Validate("not-json");
        Assert.False(result.IsValid);
        Assert.Contains("JSON", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeoPoint_Validate_Boundary_Values_Pass()
    {
        // Exact min/max should be valid
        Assert.True(GeoPoint().Validate("{\"lat\":-90,\"lng\":-180}").IsValid);
        Assert.True(GeoPoint().Validate("{\"lat\":90,\"lng\":180}").IsValid);
    }

    // ── ICustomFieldType interface export test ─────────────────────────────────

    [Fact]
    public void ICustomFieldType_Is_Publicly_Accessible()
    {
        // Compile-time check via typeof — if this compiles the interface is public.
        var type = typeof(ICustomFieldType);
        Assert.True(type.IsInterface);
        Assert.True(type.IsPublic);
    }

    // ── Multiple custom types can be registered ────────────────────────────────

    private sealed class DummyCustomFieldType : ICustomFieldType
    {
        public string TypeName => "dummy_type";
        public Type StorageType => typeof(int);
        public ValidationResult Validate(object? value) => ValidationResult.Ok();
    }

    [Fact]
    public void Multiple_CustomFieldTypes_Can_Be_Registered()
    {
        var services = new ServiceCollection();
        services.AddCustomFieldType<GeoPointFieldType>();
        services.AddCustomFieldType<DummyCustomFieldType>();

        var registry = services.BuildServiceProvider()
            .GetRequiredService<ICustomFieldTypeRegistry>();

        Assert.Equal(2, registry.GetAll().Count);
        Assert.NotNull(registry.GetByName("geo_point"));
        Assert.NotNull(registry.GetByName("dummy_type"));
    }
}
