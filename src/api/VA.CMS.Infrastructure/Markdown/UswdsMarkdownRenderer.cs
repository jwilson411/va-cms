using Markdig;

namespace VA.CMS.Infrastructure.Markdown;

/// <summary>
/// Renders CommonMark Markdown to USWDS-safe HTML using Markdig.
///
/// DisableHtml() ensures no raw HTML passthrough — consistent with how
/// FieldsJson is stored (pure Markdown, never HTML) and how the published
/// site renders content.  The preview endpoint uses this same pipeline so
/// that preview output matches what publish produces exactly — no drift.
///
/// Issue #34 / BRD FR-AUTH-08.
/// </summary>
public interface IUswdsMarkdownRenderer
{
    /// <summary>Render CommonMark Markdown to sanitized HTML.</summary>
    string Render(string markdown);
}

public sealed class UswdsMarkdownRenderer : IUswdsMarkdownRenderer
{
    private static readonly MarkdownPipeline _pipeline =
        new MarkdownPipelineBuilder()
            .UseAdvancedExtensions()
            .DisableHtml()           // blocks raw HTML injection
            .Build();

    public string Render(string markdown) =>
        string.IsNullOrWhiteSpace(markdown)
            ? string.Empty
            : Markdig.Markdown.ToHtml(markdown, _pipeline);
}
