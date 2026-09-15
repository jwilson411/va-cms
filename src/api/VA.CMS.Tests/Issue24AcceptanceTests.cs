using Microsoft.Extensions.DependencyInjection;
using VA.CMS.Infrastructure.ContentTypes;
using VA.CMS.Infrastructure.ContentTypes.BuiltIn;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #24: ContentTypeDefinitionBase and field type registry.
///
/// AC1: Abstract ContentTypeDefinitionBase exists with Name, DisplayName, TemplateId, Fields.
///   → StandardPageTypeDefinition is resolvable and has all required properties.
///
/// AC2: IFieldTypeRegistry injectable service resolves all registered types.
///   → Resolved from DI; GetAll() returns the registered types.
///
/// AC3: DI registration: builder.Services.AddContentType&lt;T&gt;().
///   → ServiceCollection extension registers the type and the registry.
///
/// AC4: All 12 built-in field types are registered.
///   → IFieldTypeRegistry.GetBuiltInFieldTypeNames() returns exactly 12 names.
/// </summary>
public class Issue24AcceptanceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IServiceProvider BuildRegistry<T>()
        where T : ContentTypeDefinitionBase, new()
    {
        var services = new ServiceCollection();
        services.AddContentType<T>();
        return services.BuildServiceProvider();
    }

    // ── AC1: ContentTypeDefinitionBase abstract class structure ───────────────

    [Fact]
    public void AC1_ContentTypeDefinitionBase_Is_Abstract()
    {
        Assert.True(typeof(ContentTypeDefinitionBase).IsAbstract,
            "ContentTypeDefinitionBase must be abstract.");
    }

    [Fact]
    public void AC1_ContentTypeDefinitionBase_Has_Name_Property()
    {
        var prop = typeof(ContentTypeDefinitionBase).GetProperty("Name");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
    }

    [Fact]
    public void AC1_ContentTypeDefinitionBase_Has_DisplayName_Property()
    {
        var prop = typeof(ContentTypeDefinitionBase).GetProperty("DisplayName");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop!.PropertyType);
    }

    [Fact]
    public void AC1_ContentTypeDefinitionBase_Has_TemplateId_Property()
    {
        var prop = typeof(ContentTypeDefinitionBase).GetProperty("TemplateId");
        Assert.NotNull(prop);
        // TemplateId is nullable string
        Assert.True(prop!.PropertyType == typeof(string),
            "TemplateId must be of type string (nullable allowed via nullability annotations).");
    }

    [Fact]
    public void AC1_ContentTypeDefinitionBase_Has_Fields_Property()
    {
        var prop = typeof(ContentTypeDefinitionBase).GetProperty("Fields");
        Assert.NotNull(prop);
        Assert.True(
            typeof(IReadOnlyList<FieldDefinition>).IsAssignableFrom(prop!.PropertyType),
            "Fields must be assignable to IReadOnlyList<FieldDefinition>.");
    }

    // ── AC1 (StandardPage): Standard Page type is defined and resolvable ─────

    [Fact]
    public void AC1_StandardPage_Name_Is_standard_page()
    {
        var type = new StandardPageTypeDefinition();
        Assert.Equal("standard_page", type.Name);
    }

    [Fact]
    public void AC1_StandardPage_DisplayName_Is_Set()
    {
        var type = new StandardPageTypeDefinition();
        Assert.False(string.IsNullOrWhiteSpace(type.DisplayName));
    }

    [Fact]
    public void AC1_StandardPage_TemplateId_Is_Set()
    {
        var type = new StandardPageTypeDefinition();
        Assert.False(string.IsNullOrWhiteSpace(type.TemplateId));
    }

    [Fact]
    public void AC1_StandardPage_Fields_Contains_At_Least_Title_And_Body()
    {
        var type = new StandardPageTypeDefinition();
        Assert.Contains(type.Fields, f => f.Name == "title");
        Assert.Contains(type.Fields, f => f.Name == "body");
    }

    [Fact]
    public void AC1_StandardPage_Title_Field_Is_Required_ShortText()
    {
        var type = new StandardPageTypeDefinition();
        var title = type.Fields.Single(f => f.Name == "title");
        Assert.Equal(FieldType.ShortText, title.Type);
        Assert.True(title.Required);
    }

    [Fact]
    public void AC1_StandardPage_Body_Field_Is_Required_RichText()
    {
        var type = new StandardPageTypeDefinition();
        var body = type.Fields.Single(f => f.Name == "body");
        Assert.Equal(FieldType.RichText, body.Type);
        Assert.True(body.Required);
    }

    // ── AC2: IFieldTypeRegistry resolves via DI ───────────────────────────────

    [Fact]
    public void AC2_Registry_Resolves_From_DI()
    {
        var sp = BuildRegistry<StandardPageTypeDefinition>();
        var registry = sp.GetRequiredService<IFieldTypeRegistry>();
        Assert.NotNull(registry);
    }

    [Fact]
    public void AC2_Registry_GetAll_Returns_Registered_Type()
    {
        var sp = BuildRegistry<StandardPageTypeDefinition>();
        var registry = sp.GetRequiredService<IFieldTypeRegistry>();

        var all = registry.GetAll();
        Assert.Single(all);
        Assert.Equal("standard_page", all[0].Name);
    }

    [Fact]
    public void AC2_Registry_GetByName_Returns_Correct_Type()
    {
        var sp = BuildRegistry<StandardPageTypeDefinition>();
        var registry = sp.GetRequiredService<IFieldTypeRegistry>();

        var type = registry.GetByName("standard_page");
        Assert.NotNull(type);
        Assert.IsType<StandardPageTypeDefinition>(type);
    }

    [Fact]
    public void AC2_Registry_GetByName_ReturnsNull_For_Unknown_Name()
    {
        var sp = BuildRegistry<StandardPageTypeDefinition>();
        var registry = sp.GetRequiredService<IFieldTypeRegistry>();

        Assert.Null(registry.GetByName("does_not_exist"));
    }

    [Fact]
    public void AC2_Registry_GetByName_Is_CaseInsensitive()
    {
        var sp = BuildRegistry<StandardPageTypeDefinition>();
        var registry = sp.GetRequiredService<IFieldTypeRegistry>();

        Assert.NotNull(registry.GetByName("Standard_Page"));
        Assert.NotNull(registry.GetByName("STANDARD_PAGE"));
    }

    // ── AC3: AddContentType<T>() DI extension ─────────────────────────────────

    [Fact]
    public void AC3_AddContentType_Registers_IFieldTypeRegistry()
    {
        var services = new ServiceCollection();
        services.AddContentType<StandardPageTypeDefinition>();
        var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<IFieldTypeRegistry>());
    }

    [Fact]
    public void AC3_AddContentType_Registers_ContentTypeDefinitionBase()
    {
        var services = new ServiceCollection();
        services.AddContentType<StandardPageTypeDefinition>();
        var sp = services.BuildServiceProvider();
        var definitions = sp.GetServices<ContentTypeDefinitionBase>().ToList();
        Assert.Single(definitions);
        Assert.IsType<StandardPageTypeDefinition>(definitions[0]);
    }

    [Fact]
    public void AC3_AddContentType_Multiple_Types_All_Registered()
    {
        var services = new ServiceCollection();
        services.AddContentType<StandardPageTypeDefinition>();
        services.AddContentType<AnotherTypeForTest>();
        var sp = services.BuildServiceProvider();

        var registry = sp.GetRequiredService<IFieldTypeRegistry>();
        Assert.Equal(2, registry.GetAll().Count);
    }

    [Fact]
    public void AC3_AddContentType_Singleton_Registry_Is_Same_Instance()
    {
        var services = new ServiceCollection();
        services.AddContentType<StandardPageTypeDefinition>();
        var sp = services.BuildServiceProvider();

        var r1 = sp.GetRequiredService<IFieldTypeRegistry>();
        var r2 = sp.GetRequiredService<IFieldTypeRegistry>();
        Assert.Same(r1, r2);
    }

    // ── AC4: All 12 built-in field types are registered ──────────────────────

    [Fact]
    public void AC4_BuiltIn_FieldTypeNames_Count_Is_13()
    {
        var services = new ServiceCollection();
        services.AddContentTypeRegistry();
        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IFieldTypeRegistry>();

        var names = registry.GetBuiltInFieldTypeNames();
        // Epic scope lists 13 types (ShortText..Json); AC says "12" but enumerates 13.
        Assert.Equal(13, names.Count);
    }

    [Fact]
    public void AC4_All_12_BuiltIn_FieldTypes_Present()
    {
        var services = new ServiceCollection();
        services.AddContentTypeRegistry();
        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IFieldTypeRegistry>();

        var names = registry.GetBuiltInFieldTypeNames().ToHashSet(StringComparer.OrdinalIgnoreCase);

        var expected = new[]
        {
            "ShortText", "LongText", "RichText", "Number", "Boolean",
            "DateTime", "Url", "Email", "MediaReference",
            "SingleRelation", "MultiRelation", "RepeatableGroup", "Json",
        };

        foreach (var name in expected)
            Assert.Contains(name, names);
    }

    [Fact]
    public void AC4_FieldType_Enum_Has_All_13_Types()
    {
        // Guard: the FieldType enum itself must contain all 13 values
        var values = Enum.GetNames<FieldType>();
        Assert.Equal(13, values.Length);
    }

    [Fact]
    public void AC4_No_Duplicate_BuiltIn_FieldTypeNames()
    {
        var services = new ServiceCollection();
        services.AddContentTypeRegistry();
        var sp = services.BuildServiceProvider();
        var registry = sp.GetRequiredService<IFieldTypeRegistry>();

        var names = registry.GetBuiltInFieldTypeNames();
        var distinct = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Assert.Equal(names.Count, distinct.Count);
    }

    // ── FieldDefinition construction guard ───────────────────────────────────

    [Fact]
    public void FieldDefinition_Blank_Name_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new FieldDefinition("", FieldType.ShortText));
    }

    [Fact]
    public void FieldDefinition_WhiteSpace_Name_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new FieldDefinition("   ", FieldType.ShortText));
    }
}

// ── Helper type for multi-type tests ────────────────────────────────────────

internal sealed class AnotherTypeForTest : ContentTypeDefinitionBase
{
    public override string Name        => "another_type";
    public override string DisplayName => "Another Type";
    public override string? TemplateId => null;
    public override IReadOnlyList<FieldDefinition> Fields =>
    [
        new FieldDefinition("value", FieldType.Number),
    ];
}
