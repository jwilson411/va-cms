using Microsoft.AspNetCore.DataProtection;

namespace VA.CMS.API.Webhooks;

/// <summary>
/// Webhook HMAC secrets at rest (#168). The clear secret is shown once at registration and
/// then only ever needed to sign a delivery, so it is stored as an ASP.NET Data Protection
/// payload ("dp1:" + base64url) and unprotected inside the dispatcher. The key ring lives
/// where DataProtection:KeysPath points (a share every node can read, DPAPI-protected on
/// Windows) — see DEPLOYMENT.md. Rows written before this change are re-keyed on the next
/// start by <see cref="WebhookSecretRekeyService"/>; <see cref="Unprotect"/> still accepts
/// clear text until then so signing never breaks mid-migration.
/// </summary>
public interface IWebhookSecretProtector
{
    string Protect(string secret);
    string Unprotect(string stored);
    bool IsProtected(string stored);
}

public sealed class WebhookSecretProtector : IWebhookSecretProtector
{
    public const string Prefix  = "dp1:";
    public const string Purpose = "VA.CMS.Webhooks.Secret.v1";

    private readonly IDataProtector _protector;

    public WebhookSecretProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector(Purpose);

    public string Protect(string secret) => Prefix + _protector.Protect(secret);

    public string Unprotect(string stored)
        => IsProtected(stored) ? _protector.Unprotect(stored[Prefix.Length..]) : stored;

    public bool IsProtected(string stored) => stored.StartsWith(Prefix, StringComparison.Ordinal);
}
