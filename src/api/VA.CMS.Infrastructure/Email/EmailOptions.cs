using System.ComponentModel.DataAnnotations;

namespace VA.CMS.Infrastructure.Email;

/// <summary>
/// SMTP connection settings — the "Email" configuration section (issue #39, BRD FR-WORKFLOW-02).
///
/// This is deliberately the only email configuration outside the database: it carries the relay
/// address and credentials, which are secrets bound to the deployment. Everything an admin may
/// want to change at runtime — the on/off switch, sender address and name, and the admin site
/// origin used for links — is a site setting (notifications.email*, notifications.adminBaseUrl).
///
/// Binds from environment variables with the standard "__" separator, e.g.
///   Email__Smtp__Host=smtp.office365.com
///   Email__Smtp__Port=587
///   Email__Smtp__Security=StartTls
///   Email__Smtp__Username=cms-noreply@va.gov
///   Email__Smtp__Password=...
///
/// Delivery is possible only when Email:Smtp:Host is set. Leave it empty (the default) and
/// workflow emails are logged instead of sent — the in-app notification center keeps working.
/// </summary>
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public SmtpOptions Smtp { get; set; } = new();

    /// <summary>True when an SMTP host is configured; false means log-only delivery.</summary>
    public bool IsEnabled => !string.IsNullOrWhiteSpace(Smtp.Host);

    /// <summary>
    /// Fail fast at startup on a half-configured relay rather than at the first workflow
    /// action, when the failure would only show up in a log line.
    /// </summary>
    public void Validate()
    {
        if (!IsEnabled) return;

        if (Smtp.Port is < 1 or > 65535)
            throw new InvalidOperationException($"Email:Smtp:Port must be between 1 and 65535 (got {Smtp.Port}).");

        if (string.IsNullOrEmpty(Smtp.Username) != string.IsNullOrEmpty(Smtp.Password))
            throw new InvalidOperationException(
                "Email:Smtp:Username and Email:Smtp:Password must be set together (or both left empty for an unauthenticated relay).");
    }
}

/// <summary>SMTP connection settings — the "Email:Smtp" section.</summary>
public sealed class SmtpOptions
{
    /// <summary>Exchange Online: smtp.office365.com. On-prem: the Exchange hub/edge server or relay.</summary>
    public string? Host { get; set; }

    /// <summary>587 for STARTTLS submission (Exchange Online and on-prem default); 25 for an internal relay.</summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    /// <summary>How TLS is negotiated. <see cref="SmtpSecurity.StartTls"/> is what Exchange expects on 587.</summary>
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    /// <summary>Leave empty for an unauthenticated internal relay (IP-allow-listed on-prem receive connector).</summary>
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Connect / command timeout.</summary>
    [Range(1, 600)]
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>Maps 1:1 onto MailKit's SecureSocketOptions so operators configure it by name.</summary>
public enum SmtpSecurity
{
    /// <summary>Plain connection upgraded with STARTTLS; fails if the server doesn't offer it. Ports 587 / 25 on Exchange.</summary>
    StartTls,
    /// <summary>Implicit TLS from the first byte. Port 465.</summary>
    SslOnConnect,
    /// <summary>Let MailKit pick based on the port (465 → SslOnConnect, otherwise STARTTLS when offered).</summary>
    Auto,
    /// <summary>No TLS at all. Only for a local dev sink such as Mailpit.</summary>
    None,
}
