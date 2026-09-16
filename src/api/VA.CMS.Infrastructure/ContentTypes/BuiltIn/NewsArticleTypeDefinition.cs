namespace VA.CMS.Infrastructure.ContentTypes.BuiltIn;

/// <summary>
/// The News Article content type (issue #59 public template, BRD FR-DEV-03).
/// Fields mirror src/public/lib/cms/content.ts NewsArticleFields and the demo seed
/// (title, summary, body); the remaining fields are optional so seeded articles
/// remain valid. Registering it in code lets the admin create and edit articles —
/// previously the type existed only as a seeded ContentType row.
/// </summary>
public sealed class NewsArticleTypeDefinition : ContentTypeDefinitionBase
{
    public override string Name        => "news_article";
    public override string DisplayName => "News Article";
    public override string? TemplateId => "NewsArticleTemplate";
    public override bool AllowWorkflow => true;

    public override IReadOnlyList<FieldDefinition> Fields =>
    [
        new FieldDefinition("title",         FieldType.ShortText,      required: true,  maxLength: 200),
        new FieldDefinition("summary",       FieldType.LongText,       required: true,  maxLength: 500),
        new FieldDefinition("body",          FieldType.RichText,       required: true),
        new FieldDefinition("featuredImage", FieldType.MediaReference, required: false, label: "Featured image"),
        new FieldDefinition("publishDate",   FieldType.DateTime,       required: false, label: "Publish date"),
        new FieldDefinition("author",        FieldType.ShortText,      required: false, maxLength: 200),
    ];
}
