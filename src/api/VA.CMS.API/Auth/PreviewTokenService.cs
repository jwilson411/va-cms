using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.API.Auth;

/// <summary>
/// Stateless signed preview tokens for issue #34 (BRD FR-AUTH-08).
///
/// A preview token encodes the content entry id and an expiry timestamp,
/// signed with HMAC-SHA256 using the same signing key as user JWTs.
/// No database table is required — the token is self-validating.
///
/// Token format (URL-safe base64):  base64url(payload_json).base64url(hmac)
/// Payload: { "entryId": long, "exp": unix_seconds }
/// TTL: 60 minutes
/// </summary>
public interface IPreviewTokenService
{
    /// <summary>Mint a preview token for the given content entry id.</summary>
    string Issue(long entryId);

    /// <summary>
    /// Validate a preview token. Returns the entry id on success, or null if
    /// the token is invalid, expired, or tampered.
    /// </summary>
    long? Validate(string token);
}

public sealed class PreviewTokenService : IPreviewTokenService
{
    private readonly byte[] _keyBytes;
    private readonly ISiteSettingsService _settings;

    /// <param name="jwtOptions">The signing key the preview subkey is derived from.</param>
    /// <param name="settings">Source of auth.previewTokenMinutes (issue #146); code default when null.</param>
    public PreviewTokenService(JwtOptions jwtOptions, ISiteSettingsService? settings = null)
    {
        _settings = settings ?? StaticSiteSettings.Defaults;
        // Derive a separate subkey so preview tokens can't be confused with user JWTs
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(jwtOptions.SigningKey));
        _keyBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes("preview-token-subkey-v1"));
    }

    public string Issue(long entryId)
    {
        var payload = new PreviewTokenPayload
        {
            EntryId = entryId,
            Exp     = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, _settings.GetInt(SiteSettingKeys.AuthPreviewTokenMinutes))).ToUnixTimeSeconds(),
            Nonce   = Base64UrlEncode(RandomNumberGenerator.GetBytes(8)),
        };

        var payloadJson = JsonSerializer.Serialize(payload);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);
        var payloadB64 = Base64UrlEncode(payloadBytes);

        var sig = ComputeSig(payloadB64);
        return $"{payloadB64}.{sig}";
    }

    public long? Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var dotIdx = token.IndexOf('.');
        if (dotIdx < 0) return null;

        var payloadB64 = token[..dotIdx];
        var sigPart    = token[(dotIdx + 1)..];

        // Constant-time comparison to prevent timing attacks
        var expectedSig = ComputeSig(payloadB64);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(sigPart),
                Encoding.UTF8.GetBytes(expectedSig)))
            return null;

        try
        {
            var payloadBytes = Base64UrlDecode(payloadB64);
            var payload = JsonSerializer.Deserialize<PreviewTokenPayload>(payloadBytes);
            if (payload is null) return null;

            // Check expiry
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (payload.Exp < now) return null;

            return payload.EntryId;
        }
        catch
        {
            return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string ComputeSig(string payloadB64)
    {
        using var hmac = new HMACSHA256(_keyBytes);
        var sigBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
        return Base64UrlEncode(sigBytes);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
               .TrimEnd('=')
               .Replace('+', '-')
               .Replace('/', '_');

    private static byte[] Base64UrlDecode(string s)
    {
        // Re-add padding
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "=";  break;
        }
        return Convert.FromBase64String(s);
    }

    // ── Payload model (private, serialized to JSON inside the token) ──────────

    private sealed class PreviewTokenPayload
    {
        public long EntryId { get; set; }
        /// <summary>Unix epoch seconds — expiry.</summary>
        public long Exp { get; set; }
        /// <summary>Random nonce — ensures uniqueness even within the same second.</summary>
        public string Nonce { get; set; } = string.Empty;
    }
}
