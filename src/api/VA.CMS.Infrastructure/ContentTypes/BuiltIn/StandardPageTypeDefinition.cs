namespace VA.CMS.Infrastructure.ContentTypes.BuiltIn;

/// <summary>
/// The Standard Page content type — the canonical built-in page type for
/// the VA CMS. Demonstrates the code-first type definition pattern.
/// </summary>
public sealed class StandardPageTypeDefinition : ContentTypeDefinitionBase
{
    public override string Name        => "standard_page";
    public override string DisplayName => "Standard Page";
    public override string? TemplateId => "StandardPageTemplate";
    public override bool AllowWorkflow => true;

    public override IReadOnlyList<FieldDefinition> Fields =>
    [
        new FieldDefinition("title",     FieldType.ShortText,  required: true,  maxLength: 200),
        new FieldDefinition("summary",   FieldType.LongText,   required: false, maxLength: 500),
        new FieldDefinition("body",      FieldType.RichText,   required: true),
        new FieldDefinition("heroImage", FieldType.MediaReference, required: false),
    ];
}
