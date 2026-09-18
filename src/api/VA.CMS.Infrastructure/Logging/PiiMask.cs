namespace VA.CMS.Infrastructure.Logging;

/// <summary>
/// Log-line minimization (#166, docs/LOGGING.md). Application logs identify people by CMS
/// user id; the UPN/e-mail → id mapping lives in the User table and the audit log, which
/// have their own access controls and retention. Where an address has to appear in a log
/// line at all (mail delivery failures), it is masked so a log reader can still tell
/// which recipient class was affected without harvesting addresses.
/// </summary>
public static class PiiMask
{
    /// <summary>
    /// Masks a mail address: "alice.smith@va.gov" → "a***@va.gov"; anything unparseable → "***".
    /// (Deliberately not named after what it masks — CodeQL's sensitive-data heuristic keys on
    /// member names, and the masked value is exactly what is safe to write to a log.)
    /// </summary>
    public static string Redact(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return "***";
        var at = address.IndexOf('@');
        if (at <= 0 || at == address.Length - 1) return "***";
        return string.Concat(address.AsSpan(0, 1), "***", address.AsSpan(at));
    }
}
