using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using VA.CMS.Infrastructure.ContentTypes;
using VA.CMS.Infrastructure.ContentTypes.BuiltIn;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #26:
/// Build admin content type browser and field schema viewer (FR-SCHEMA-06).
///
/// AC:
/// - GET /api/v1/admin/content-types lists all registered types
/// - GET /api/v1/admin/content-types/{name} returns display name, description, and ordered field list
/// - Field list includes: name, type, required flag, constraints (maxLength)
/// - IFieldTypeRegistry is injectable and returns all registered types
/// </summary>
public class Issue26AcceptanceTests
{
    // ── IFieldTypeRegistry unit tests ─────────────────────────────────────────

    private IFieldTypeRegistry BuildRegistry()
    {
        var services = new ServiceCollection();
        services.AddContentType<StandardPageTypeDefinition>();
        return services.BuildServiceProvider().GetRequiredService<IFieldTypeRegistry>();
    }

    [Fact]
    public void Registry_GetAll_Returns_Registered_Types()
    {
        var registry = BuildRegistry();
        var types = registry.GetAll();
        Assert.NotEmpty(types);
        Assert.Contains(types, t => t.Name == "standard_page");
    }

    [Fact]
    public void Registry_GetByName_Returns_Type_For_Known_Name()
    {
        var registry = BuildRegistry();
        var type = registry.GetByName("standard_page");
        Assert.NotNull(type);
        Assert.Equal("Standard Page", type.DisplayName);
    }

    [Fact]
    public void Registry_GetByName_Returns_Null_For_Unknown_Name()
    {
        var registry = BuildRegistry();
        var type = registry.GetByName("no_such_type");
        Assert.Null(type);
    }

    [Fact]
    public void Registry_GetByName_Is_Case_Insensitive()
    {
        var registry = BuildRegistry();
        var type = registry.GetByName("STANDARD_PAGE");
        Assert.NotNull(type);
    }

    [Fact]
    public void StandardPage_Has_Expected_Fields()
    {
        var registry = BuildRegistry();
        var type = registry.GetByName("standard_page")!;
        var fields = type.Fields;

        Assert.NotEmpty(fields);
        Assert.Contains(fields, f => f.Name == "title"   && f.Required && f.Type == FieldType.ShortText);
        Assert.Contains(fields, f => f.Name == "summary" && !f.Required && f.Type == FieldType.LongText);
        Assert.Contains(fields, f => f.Name == "body"    && f.Required && f.Type == FieldType.RichText);
        Assert.Contains(fields, f => f.Name == "heroImage" && !f.Required && f.Type == FieldType.MediaReference);
    }

    [Fact]
    public void StandardPage_Fields_Have_MaxLength_Constraints()
    {
        var registry = BuildRegistry();
        var type = registry.GetByName("standard_page")!;

        var title   = type.Fields.First(f => f.Name == "title");
        var summary = type.Fields.First(f => f.Name == "summary");

        Assert.Equal(200, title.MaxLength);
        Assert.Equal(500, summary.MaxLength);
    }

    [Fact]
    public void StandardPage_Fields_Are_Ordered()
    {
        var registry = BuildRegistry();
        var type = registry.GetByName("standard_page")!;
        var names = type.Fields.Select(f => f.Name).ToList();

        // Registration order must be preserved
        var titleIndex  = names.IndexOf("title");
        var summaryIndex = names.IndexOf("summary");
        var bodyIndex   = names.IndexOf("body");

        Assert.True(titleIndex < summaryIndex, "title should come before summary");
        Assert.True(summaryIndex < bodyIndex,  "summary should come before body");
    }

    [Fact]
    public void BuiltInFieldTypeNames_Contains_All_13()
    {
        var registry = BuildRegistry();
        var names = registry.GetBuiltInFieldTypeNames();

        Assert.Equal(13, names.Count);
        Assert.Contains("ShortText",       names);
        Assert.Contains("LongText",        names);
        Assert.Contains("RichText",        names);
        Assert.Contains("Number",          names);
        Assert.Contains("Boolean",         names);
        Assert.Contains("DateTime",        names);
        Assert.Contains("Url",             names);
        Assert.Contains("Email",           names);
        Assert.Contains("MediaReference",  names);
        Assert.Contains("SingleRelation",  names);
        Assert.Contains("MultiRelation",   names);
        Assert.Contains("RepeatableGroup", names);
        Assert.Contains("Json",            names);
    }
}
