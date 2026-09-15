using Microsoft.Extensions.DependencyInjection;
using VA.CMS.Infrastructure.ContentTypes;
using VA.CMS.Infrastructure.ContentTypes.Validation;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #25:
/// Implement all 12 built-in field types with validation (FR-SCHEMA-02).
///
/// AC: ShortText, LongText, RichText, Number, Boolean, DateTime, URL, Email each
///     validate correct input and reject invalid.
///     MediaReference resolves to a MediaAsset by ID.
///     SingleRelation and MultiRelation resolve ContentEntry references.
///     RepeatableGroup supports nested field definitions.
///     JSON field accepts valid JSON, rejects malformed.
/// </summary>
public class Issue25AcceptanceTests
{
    private readonly IFieldValidator _validator;

    public Issue25AcceptanceTests()
    {
        var services = new ServiceCollection();
        services.AddContentTypeRegistry();
        var sp = services.BuildServiceProvider();
        _validator = sp.GetRequiredService<IFieldValidator>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private FieldDefinition Field(string name, FieldType type, bool required = false,
        int? maxLength = null, object? options = null)
        => new(name, type, required, maxLength, options: options);

    private void AssertValid(FieldDefinition field, object? value) =>
        Assert.True(_validator.Validate(field, value).IsValid,
            $"Expected valid for {field.Type} value '{value}'");

    private void AssertInvalid(FieldDefinition field, object? value) =>
        Assert.False(_validator.Validate(field, value).IsValid,
            $"Expected invalid for {field.Type} value '{value}'");

    // ── IFieldValidator is injectable ─────────────────────────────────────────

    [Fact]
    public void IFieldValidator_Resolves_From_DI()
    {
        var services = new ServiceCollection();
        services.AddContentTypeRegistry();
        var sp = services.BuildServiceProvider();
        Assert.NotNull(sp.GetService<IFieldValidator>());
    }

    // ── Required field check ─────────────────────────────────────────────────

    [Fact]
    public void Required_Field_Null_Value_Is_Invalid()
    {
        var field = Field("title", FieldType.ShortText, required: true);
        AssertInvalid(field, null);
    }

    [Fact]
    public void Required_Field_Blank_String_Is_Invalid()
    {
        var field = Field("title", FieldType.ShortText, required: true);
        AssertInvalid(field, "   ");
    }

    [Fact]
    public void Optional_Field_Null_Value_Is_Valid()
    {
        var field = Field("subtitle", FieldType.ShortText, required: false);
        AssertValid(field, null);
    }

    // ── ShortText ─────────────────────────────────────────────────────────────

    [Fact]
    public void ShortText_Valid_String()
    {
        AssertValid(Field("title", FieldType.ShortText), "Hello world");
    }

    [Fact]
    public void ShortText_Exceeds_MaxLength_Is_Invalid()
    {
        var field = Field("title", FieldType.ShortText, maxLength: 5);
        AssertInvalid(field, "Too long string");
    }

    [Fact]
    public void ShortText_Within_MaxLength_Is_Valid()
    {
        var field = Field("title", FieldType.ShortText, maxLength: 20);
        AssertValid(field, "Short");
    }

    [Fact]
    public void ShortText_Non_String_Is_Invalid()
    {
        AssertInvalid(Field("title", FieldType.ShortText), 42);
    }

    // ── LongText ─────────────────────────────────────────────────────────────

    [Fact]
    public void LongText_Valid_String()
    {
        AssertValid(Field("summary", FieldType.LongText), "A longer piece of text with details.");
    }

    [Fact]
    public void LongText_Exceeds_MaxLength_Is_Invalid()
    {
        var field = Field("summary", FieldType.LongText, maxLength: 10);
        AssertInvalid(field, "This is definitely too long for the field");
    }

    [Fact]
    public void LongText_Non_String_Is_Invalid()
    {
        AssertInvalid(Field("summary", FieldType.LongText), 3.14);
    }

    // ── RichText ─────────────────────────────────────────────────────────────

    [Fact]
    public void RichText_Valid_Markdown()
    {
        AssertValid(Field("body", FieldType.RichText), "## Heading\n\nSome **bold** text.");
    }

    [Fact]
    public void RichText_Non_String_Is_Invalid()
    {
        AssertInvalid(Field("body", FieldType.RichText), 999);
    }

    [Fact]
    public void RichText_Empty_Non_Required_Is_Valid()
    {
        // Empty string is treated as blank — ok for optional
        var field = Field("body", FieldType.RichText, required: false);
        AssertValid(field, null);
    }

    // ── Number ───────────────────────────────────────────────────────────────

    [Fact]
    public void Number_Integer_Is_Valid()
    {
        AssertValid(Field("count", FieldType.Number), 42);
    }

    [Fact]
    public void Number_Double_Is_Valid()
    {
        AssertValid(Field("price", FieldType.Number), 3.14);
    }

    [Fact]
    public void Number_Decimal_Is_Valid()
    {
        AssertValid(Field("amount", FieldType.Number), 100.50m);
    }

    [Fact]
    public void Number_Numeric_String_Is_Valid()
    {
        AssertValid(Field("count", FieldType.Number), "42");
    }

    [Fact]
    public void Number_Non_Numeric_String_Is_Invalid()
    {
        AssertInvalid(Field("count", FieldType.Number), "not-a-number");
    }

    [Fact]
    public void Number_Boolean_Is_Invalid()
    {
        AssertInvalid(Field("count", FieldType.Number), true);
    }

    // ── Boolean ──────────────────────────────────────────────────────────────

    [Fact]
    public void Boolean_True_Is_Valid()
    {
        AssertValid(Field("active", FieldType.Boolean), true);
    }

    [Fact]
    public void Boolean_False_Is_Valid()
    {
        AssertValid(Field("active", FieldType.Boolean), false);
    }

    [Fact]
    public void Boolean_String_True_Is_Valid()
    {
        AssertValid(Field("active", FieldType.Boolean), "true");
    }

    [Fact]
    public void Boolean_String_False_Is_Valid()
    {
        AssertValid(Field("active", FieldType.Boolean), "false");
    }

    [Fact]
    public void Boolean_String_Zero_Is_Valid()
    {
        AssertValid(Field("active", FieldType.Boolean), "0");
    }

    [Fact]
    public void Boolean_String_One_Is_Valid()
    {
        AssertValid(Field("active", FieldType.Boolean), "1");
    }

    [Fact]
    public void Boolean_Random_String_Is_Invalid()
    {
        AssertInvalid(Field("active", FieldType.Boolean), "yes");
    }

    [Fact]
    public void Boolean_Number_Is_Invalid()
    {
        AssertInvalid(Field("active", FieldType.Boolean), 42);
    }

    // ── DateTime ─────────────────────────────────────────────────────────────

    [Fact]
    public void DateTime_CLR_DateTime_Is_Valid()
    {
        AssertValid(Field("publishDate", FieldType.DateTime), System.DateTime.UtcNow);
    }

    [Fact]
    public void DateTime_ISO8601_String_Is_Valid()
    {
        AssertValid(Field("publishDate", FieldType.DateTime), "2026-09-15T06:00:00Z");
    }

    [Fact]
    public void DateTime_DateOnly_String_Is_Valid()
    {
        AssertValid(Field("publishDate", FieldType.DateTime), "2026-09-15");
    }

    [Fact]
    public void DateTime_Invalid_String_Is_Invalid()
    {
        AssertInvalid(Field("publishDate", FieldType.DateTime), "not-a-date");
    }

    [Fact]
    public void DateTime_Number_Is_Invalid()
    {
        AssertInvalid(Field("publishDate", FieldType.DateTime), 1234567890);
    }

    // ── URL ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Url_Https_Is_Valid()
    {
        AssertValid(Field("link", FieldType.Url), "https://www.va.gov/health");
    }

    [Fact]
    public void Url_Http_Is_Valid()
    {
        AssertValid(Field("link", FieldType.Url), "http://internal.va.gov");
    }

    [Fact]
    public void Url_Relative_Is_Invalid()
    {
        AssertInvalid(Field("link", FieldType.Url), "/relative/path");
    }

    [Fact]
    public void Url_FTP_Scheme_Is_Invalid()
    {
        AssertInvalid(Field("link", FieldType.Url), "ftp://files.va.gov/something");
    }

    [Fact]
    public void Url_Bare_Text_Is_Invalid()
    {
        AssertInvalid(Field("link", FieldType.Url), "not a url");
    }

    [Fact]
    public void Url_Non_String_Is_Invalid()
    {
        AssertInvalid(Field("link", FieldType.Url), 12345);
    }

    // ── Email ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Email_Valid_Address_Is_Valid()
    {
        AssertValid(Field("contact", FieldType.Email), "user@va.gov");
    }

    [Fact]
    public void Email_Valid_Plus_Address_Is_Valid()
    {
        AssertValid(Field("contact", FieldType.Email), "user+tag@example.com");
    }

    [Fact]
    public void Email_Missing_At_Is_Invalid()
    {
        AssertInvalid(Field("contact", FieldType.Email), "notanemail");
    }

    [Fact]
    public void Email_Missing_Domain_Is_Invalid()
    {
        AssertInvalid(Field("contact", FieldType.Email), "user@");
    }

    [Fact]
    public void Email_Non_String_Is_Invalid()
    {
        AssertInvalid(Field("contact", FieldType.Email), 42);
    }

    // ── MediaReference ────────────────────────────────────────────────────────

    [Fact]
    public void MediaReference_Positive_Long_Id_Is_Valid()
    {
        AssertValid(Field("image", FieldType.MediaReference), 1L);
    }

    [Fact]
    public void MediaReference_Positive_String_Id_Is_Valid()
    {
        AssertValid(Field("image", FieldType.MediaReference), "42");
    }

    [Fact]
    public void MediaReference_Zero_Id_Is_Invalid()
    {
        AssertInvalid(Field("image", FieldType.MediaReference), 0L);
    }

    [Fact]
    public void MediaReference_Negative_Id_Is_Invalid()
    {
        AssertInvalid(Field("image", FieldType.MediaReference), -5L);
    }

    [Fact]
    public void MediaReference_Non_Numeric_String_Is_Invalid()
    {
        AssertInvalid(Field("image", FieldType.MediaReference), "not-an-id");
    }

    // ── SingleRelation ────────────────────────────────────────────────────────

    [Fact]
    public void SingleRelation_Positive_Id_Is_Valid()
    {
        AssertValid(Field("relatedPage", FieldType.SingleRelation), 100L);
    }

    [Fact]
    public void SingleRelation_String_Id_Is_Valid()
    {
        AssertValid(Field("relatedPage", FieldType.SingleRelation), "55");
    }

    [Fact]
    public void SingleRelation_Zero_Is_Invalid()
    {
        AssertInvalid(Field("relatedPage", FieldType.SingleRelation), 0);
    }

    [Fact]
    public void SingleRelation_Negative_Is_Invalid()
    {
        AssertInvalid(Field("relatedPage", FieldType.SingleRelation), -1L);
    }

    // ── MultiRelation ─────────────────────────────────────────────────────────

    [Fact]
    public void MultiRelation_Array_Of_Valid_Ids_Is_Valid()
    {
        AssertValid(Field("relatedPages", FieldType.MultiRelation), new long[] { 1L, 2L, 3L });
    }

    [Fact]
    public void MultiRelation_Json_Array_String_Is_Valid()
    {
        AssertValid(Field("relatedPages", FieldType.MultiRelation), "[1,2,3]");
    }

    [Fact]
    public void MultiRelation_Array_With_Zero_Id_Is_Invalid()
    {
        AssertInvalid(Field("relatedPages", FieldType.MultiRelation), new long[] { 1L, 0L });
    }

    [Fact]
    public void MultiRelation_Malformed_Json_Is_Invalid()
    {
        AssertInvalid(Field("relatedPages", FieldType.MultiRelation), "{not-array}");
    }

    [Fact]
    public void MultiRelation_Non_Array_Type_Is_Invalid()
    {
        AssertInvalid(Field("relatedPages", FieldType.MultiRelation), 42);
    }

    // ── RepeatableGroup ───────────────────────────────────────────────────────

    [Fact]
    public void RepeatableGroup_Valid_Json_Array_With_Options_Is_Valid()
    {
        var opts = new RepeatableGroupOptions(new[]
        {
            new FieldDefinition("name", FieldType.ShortText, required: true),
            new FieldDefinition("value", FieldType.Number),
        });
        var field = Field("links", FieldType.RepeatableGroup, options: opts);
        AssertValid(field, "[{\"name\":\"Home\",\"value\":1}]");
    }

    [Fact]
    public void RepeatableGroup_Empty_Array_Is_Valid()
    {
        var opts = new RepeatableGroupOptions(new[]
        {
            new FieldDefinition("label", FieldType.ShortText),
        });
        var field = Field("items", FieldType.RepeatableGroup, options: opts);
        // Empty array is structurally valid — business rule enforces min rows elsewhere
        AssertValid(field, "[]");
    }

    [Fact]
    public void RepeatableGroup_Non_Array_Json_Is_Invalid()
    {
        var opts = new RepeatableGroupOptions(new[]
        {
            new FieldDefinition("label", FieldType.ShortText),
        });
        var field = Field("items", FieldType.RepeatableGroup, options: opts);
        AssertInvalid(field, "{\"key\":\"value\"}");
    }

    [Fact]
    public void RepeatableGroup_Missing_Options_Is_Invalid()
    {
        // No options → field is misconfigured
        var field = Field("items", FieldType.RepeatableGroup);
        AssertInvalid(field, "[{\"label\":\"Row 1\"}]");
    }

    [Fact]
    public void RepeatableGroupOptions_No_Fields_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new RepeatableGroupOptions(Array.Empty<FieldDefinition>()));
    }

    // ── JSON ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Json_Valid_Object_Is_Valid()
    {
        AssertValid(Field("meta", FieldType.Json), "{\"key\":\"value\",\"count\":1}");
    }

    [Fact]
    public void Json_Valid_Array_Is_Valid()
    {
        AssertValid(Field("meta", FieldType.Json), "[1,2,3]");
    }

    [Fact]
    public void Json_Valid_Null_Json_Is_Valid()
    {
        AssertValid(Field("meta", FieldType.Json), "null");
    }

    [Fact]
    public void Json_Malformed_Is_Invalid()
    {
        AssertInvalid(Field("meta", FieldType.Json), "{\"key\": missing-quote}");
    }

    [Fact]
    public void Json_Unclosed_Brace_Is_Invalid()
    {
        AssertInvalid(Field("meta", FieldType.Json), "{\"key\":\"value\"");
    }

    [Fact]
    public void Json_Non_String_Is_Invalid()
    {
        AssertInvalid(Field("meta", FieldType.Json), 42);
    }

    [Fact]
    public void Json_Plain_Text_Is_Invalid()
    {
        AssertInvalid(Field("meta", FieldType.Json), "not json at all");
    }

    // ── Validation error messages are non-empty ───────────────────────────────

    [Fact]
    public void Invalid_Result_Has_NonEmpty_Error_Message()
    {
        var result = _validator.Validate(
            Field("title", FieldType.ShortText, required: true), null);
        Assert.False(result.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public void Valid_Result_Has_No_Error_Message()
    {
        var result = _validator.Validate(
            Field("title", FieldType.ShortText), "Hello");
        Assert.True(result.IsValid);
        Assert.Null(result.Error);
    }
}
