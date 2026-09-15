using Markdig;

namespace VA.CMS.Infrastructure.Markdown;

/// <summary>
/// Issue #66 — BRD FR-AUTH-02a/02b.
/// Canonical interface for server-side Markdown rendering.
/// The same pipeline powers both live preview and publish — no drift.
/// </summary>
public interface IMarkdownRenderer
{
    /// <summary>Render CommonMark Markdown to sanitized HTML.</summary>
    string Render(string markdown);
}

/// <summary>
/// Renders CommonMark Markdown to USWDS-safe HTML using Markdig.
/// DisableHtml() ensures no raw HTML passthrough.
/// Issue #34 / BRD FR-AUTH-08 and Issue #66 / BRD FR-AUTH-02a/02b.
/// </summary>
public interface IUswdsMarkdownRenderer : IMarkdownRenderer
{
    // Inherits Render(string) from IMarkdownRenderer.
    // Kept for backward compatibility with existing DI registrations.
}

public sealed class UswdsMarkdownRenderer : IUswdsMarkdownRenderer, IMarkdownRenderer
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
