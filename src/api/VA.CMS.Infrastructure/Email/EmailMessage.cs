namespace VA.CMS.Infrastructure.Email;

/// <summary>
/// One outbound email: a single recipient, a plain-text body and an optional HTML alternative.
/// Issue #39.
/// </summary>
public sealed record EmailMessage(
    string  ToAddress,
    string? ToName,
    string  Subject,
    string  TextBody,
    string? HtmlBody = null);
