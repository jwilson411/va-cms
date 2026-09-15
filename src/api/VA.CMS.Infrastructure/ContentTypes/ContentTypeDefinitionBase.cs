namespace VA.CMS.Infrastructure.ContentTypes;

/// <summary>
/// Abstract base for all code-first content type definitions (FR-SCHEMA-01).
/// Derive from this class, override the abstract properties, and register via
/// <c>builder.Services.AddContentType&lt;T&gt;()</c>.
///
/// Example:
/// <code>
/// public class StandardPageTypeDefinition : ContentTypeDefinitionBase
/// {
///     public override string Name        => "standard_page";
///     public override string DisplayName => "Standard Page";
///     public override string? TemplateId => "StandardPageTemplate";
///     public override IReadOnlyList&lt;FieldDefinition&gt; Fields => new[]
///     {
///         new FieldDefinition("title", FieldType.ShortText, required: true, maxLength: 200),
///         new FieldDefinition("body",  FieldType.RichText,  required: true),
///     };
/// }
/// </code>
/// </summary>
public abstract class ContentTypeDefinitionBase
{
    /// <summary>Machine-readable type identifier (snake_case, e.g. "standard_page").</summary>
    public abstract string Name { get; }

    /// <summary>Human-readable label shown in the admin type browser.</summary>
    public abstract string DisplayName { get; }

    /// <summary>
    /// Template ID used by the public rendering pipeline to pick the React component.
    /// Null if this type has no public-facing rendering template.
    /// </summary>
    public abstract string? TemplateId { get; }

    /// <summary>Ordered field definitions for this content type.</summary>
    public abstract IReadOnlyList<FieldDefinition> Fields { get; }

    /// <summary>Whether content entries of this type go through the review/approval workflow.</summary>
    public virtual bool AllowWorkflow => true;
}
