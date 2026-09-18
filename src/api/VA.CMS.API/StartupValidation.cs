using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Data.Common;
using VA.CMS.API.Auth;
using VA.CMS.API.Observability;
using VA.CMS.Infrastructure.Email;
using VA.CMS.Infrastructure.Storage;

namespace VA.CMS.API;

/// <summary>
/// Fail-fast configuration validation (#173). Every rule that used to be an ad-hoc
/// <c>throw</c> in Program.cs lives here so a misconfigured host reports *all* of its
/// problems as one numbered list instead of failing on the first one, restarting, and
/// failing on the next. <see cref="Evaluate"/> is pure (configuration in, problems
/// out) and each rule is a public static so it can be unit-tested on its own.
///
/// Environment semantics, matching the rest of the code base:
///   Development — local machines only; the relaxed rules apply.
///   Production  — the strictest rules (TLS to SQL, no SA login, no disabled scanner).
///   Anything else (Staging, UAT, …) — treated like Production except where the
///   rule says "Production only".
/// </summary>
public static class StartupValidation
{
    /// <summary>Signing-key values shipped in example files; a copied example must never start.</summary>
    private static readonly string[] SigningKeyPlaceholderFragments =
    [
        "REPLACE_WITH", "set-via-environment", "CHANGE_ME", "CHANGEME", "your-secret", "example", "placeholder",
    ];

    public const int MinSigningKeyBytes = 32;

    /// <summary>Runs every rule and throws with a numbered list when any fails.</summary>
    public static void Run(IConfiguration configuration, IHostEnvironment environment)
    {
        var problems = Evaluate(configuration, environment.EnvironmentName, environment.ContentRootPath);
        if (problems.Count == 0) return;

        var sb = new StringBuilder();
        sb.Append("The API refused to start: ").Append(problems.Count).Append(problems.Count == 1 ? " configuration problem" : " configuration problems")
          .Append(" (ASPNETCORE_ENVIRONMENT=").Append(environment.EnvironmentName).Append("):");
        for (var i = 0; i < problems.Count; i++)
            sb.AppendLine().Append("  ").Append(i + 1).Append(". ").Append(problems[i]);
        throw new InvalidOperationException(sb.ToString());
    }

    /// <summary>Evaluates every rule against the configuration. Returns an empty list when the host may start.</summary>
    public static IReadOnlyList<string> Evaluate(IConfiguration configuration, string environmentName, string? contentRootPath = null)
    {
        var isDevelopment = string.Equals(environmentName, Environments.Development, StringComparison.OrdinalIgnoreCase);
        var isProduction  = string.Equals(environmentName, Environments.Production,  StringComparison.OrdinalIgnoreCase);

        var auth    = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        var jwt     = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
        var storage = configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>() ?? new StorageOptions();
        var scanner = configuration.GetSection(MediaScannerOptions.SectionName).Get<MediaScannerOptions>() ?? new MediaScannerOptions();
        var email   = configuration.GetSection(EmailOptions.SectionName).Get<EmailOptions>() ?? new EmailOptions();
        var sinks   = configuration.GetSection(LoggingSinkOptions.SectionName).Get<LoggingSinkOptions>() ?? new LoggingSinkOptions();

        var problems = new List<string?>
        {
            ValidateConnectionStringPresent(configuration.GetConnectionString("DefaultConnection")),
            ValidateConnectionStringSecurity(configuration.GetConnectionString("DefaultConnection"), isProduction),
            ValidateSigningKey(jwt.SigningKey, isDevelopment),
            ValidateAuthMode(auth, configuration, isDevelopment),
            ValidateAzureAdCallbackPath(auth.Mode, configuration["AzureAd:CallbackPath"]),
            HostHardeningOptions.ValidateAllowedHosts(configuration["AllowedHosts"], isDevelopment),
            HostHardeningOptions.ValidateHttpsAvailable(configuration, isDevelopment),
            storage.Validate(isDevelopment ? null : contentRootPath),
            ValidateScanner(scanner, isProduction),
            ValidateSmtp(email, isDevelopment),
        };

        // Forwarded-header trust parses CIDRs; a malformed entry is a configuration problem too.
        try { HostHardeningOptions.BuildForwardedHeaders(configuration); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException) { problems.Add(ex.Message); }

        problems.AddRange(ValidateAnnotations(jwt,     JwtOptions.SectionName));
        problems.AddRange(ValidateAnnotations(auth,    AuthOptions.SectionName));
        problems.AddRange(ValidateAnnotations(storage, StorageOptions.SectionName));
        problems.AddRange(ValidateAnnotations(scanner, MediaScannerOptions.SectionName));
        problems.AddRange(ValidateAnnotations(email.Smtp, $"{EmailOptions.SectionName}:Smtp"));
        problems.AddRange(sinks.Validate(isDevelopment));
        problems.AddRange(ValidateAnnotations(sinks.File,     $"{LoggingSinkOptions.SectionName}:File"));
        problems.AddRange(ValidateAnnotations(sinks.EventLog, $"{LoggingSinkOptions.SectionName}:EventLog"));
        problems.AddRange(ValidateAnnotations(sinks.Splunk,   $"{LoggingSinkOptions.SectionName}:Splunk"));

        return problems.Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();
    }

    // ── rules ────────────────────────────────────────────────────────────────

    public static string? ValidateConnectionStringPresent(string? connectionString)
        => string.IsNullOrWhiteSpace(connectionString)
            ? "ConnectionStrings:DefaultConnection is missing. Copy appsettings.Development.json.example to " +
              "appsettings.Development.json locally, or set ConnectionStrings__DefaultConnection on the host."
            : null;

    /// <summary>
    /// Production must verify TLS to SQL Server (no TrustServerCertificate, no Encrypt=False) and must
    /// not connect as SA — the least-privilege model (#157) exists so the API runs as vacms_app.
    /// </summary>
    public static string? ValidateConnectionStringSecurity(string? connectionString, bool isProduction)
    {
        if (!isProduction || string.IsNullOrWhiteSpace(connectionString)) return null;

        // Parsed with the provider-neutral builder on purpose: this rule only *reads* the string
        // (the API never opens a connection from here), and the SqlClient builder is the sink that
        // CodeQL cs/insecure-sql-connection watches. Keyword synonyms follow Microsoft.Data.SqlClient.
        DbConnectionStringBuilder csb;
        try { csb = new DbConnectionStringBuilder { ConnectionString = connectionString }; }
        catch (ArgumentException ex)
        {
            return $"ConnectionStrings:DefaultConnection could not be parsed: {ex.Message}";
        }

        var faults = new List<string>();
        if (IsTrue(Keyword(csb, "TrustServerCertificate", "Trust Server Certificate")))
            faults.Add("TrustServerCertificate=True disables certificate validation");
        if (!IsEncrypted(Keyword(csb, "Encrypt")))
            faults.Add("Encrypt=False sends data to SQL Server in clear text");
        if (string.Equals(Keyword(csb, "User ID", "UID", "User"), "sa", StringComparison.OrdinalIgnoreCase))
            faults.Add("User Id=sa; the API must run as the EXECUTE-only vacms_app login (#157)");

        return faults.Count == 0
            ? null
            : "ConnectionStrings:DefaultConnection is not acceptable in Production: " + string.Join("; ", faults) +
              ". Use Encrypt=True;TrustServerCertificate=False with a server certificate from the VA PKI.";

        static string? Keyword(DbConnectionStringBuilder b, params string[] names)
        {
            foreach (var n in names)
                if (b.TryGetValue(n, out var v) && v is not null) return v.ToString()?.Trim();
            return null;
        }

        // SqlClient booleans accept true/false/yes/no.
        static bool IsTrue(string? v)
            => v is not null && (v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("yes", StringComparison.OrdinalIgnoreCase));

        // Encrypt defaults to Mandatory (true) since Microsoft.Data.SqlClient 4.0; Optional/False/No is the
        // only way to turn it off, and Strict is the TDS 8 always-encrypted-transport mode.
        static bool IsEncrypted(string? v)
            => v is null || !(v.Equals("false", StringComparison.OrdinalIgnoreCase)
                              || v.Equals("no", StringComparison.OrdinalIgnoreCase)
                              || v.Equals("optional", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// HS256 needs ≥ 256 bits of key material, and the preview-token subkey (#34) is derived from the
    /// same secret. An empty key is tolerated only in Development, where Program.cs generates one.
    /// </summary>
    public static string? ValidateSigningKey(string? signingKey, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            return isDevelopment
                ? null
                : "Jwt:SigningKey is required outside Development (set Jwt__SigningKey on the host; 32+ random bytes, " +
                  "e.g. `openssl rand -base64 48`). Without it tokens would not survive a restart or a second node.";
        }

        foreach (var fragment in SigningKeyPlaceholderFragments)
        {
            if (signingKey.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                return $"Jwt:SigningKey is the example placeholder ('{fragment}…'). Generate a real secret (`openssl rand -base64 48`).";
        }

        var bytes = Encoding.UTF8.GetByteCount(signingKey);
        if (bytes < MinSigningKeyBytes)
            return $"Jwt:SigningKey is {bytes} bytes; HS256 requires at least {MinSigningKeyBytes} bytes (256 bits).";

        if (signingKey.Distinct().Count() < 8)
            return "Jwt:SigningKey has almost no entropy (fewer than 8 distinct characters). Generate a random secret.";

        return null;
    }

    /// <summary>DevBypass and the fake AD handlers are Development-only (#164); DevBypass also needs an allow-list.</summary>
    public static string? ValidateAuthMode(AuthOptions auth, IConfiguration configuration, bool isDevelopment)
    {
        var faults = new List<string>();

        if (auth.Mode == AuthMode.DevBypass)
        {
            if (!isDevelopment)
                faults.Add("Auth:Mode=DevBypass is only permitted in the Development environment; set Auth:Mode=WindowsAuth (or AzureAd for AD FS)");
            if (auth.DevBypassAllowedUsers.Length == 0)
                faults.Add("Auth:DevBypassAllowedUsers is empty — with DevBypass every UPN would be accepted; list the developer UPNs allowed to sign in");
        }

        if (!isDevelopment)
        {
            if (configuration["WINDOWS_AUTH_FAKE_NEGOTIATE"] == "true")
                faults.Add("WINDOWS_AUTH_FAKE_NEGOTIATE=true is only permitted in the Development environment");
            if (configuration["AZUREAD_FAKE_OIDC"] == "true")
                faults.Add("AZUREAD_FAKE_OIDC=true is only permitted in the Development environment");
        }

        return faults.Count == 0 ? null : string.Join(". ", faults) + ".";
    }

    /// <summary>The OIDC redirect URI is owned by the handler and must not shadow an API route (#154).</summary>
    public static string? ValidateAzureAdCallbackPath(AuthMode mode, string? callbackPath)
    {
        if (mode != AuthMode.AzureAd || string.IsNullOrEmpty(callbackPath)) return null;
        return callbackPath.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            ? $"AzureAd:CallbackPath '{callbackPath}' collides with the API routes. Leave it unset (defaults to " +
              $"{AzureAdSchemes.CallbackPath}) and register that path as the redirect URI in the app registration."
            : null;
    }

    /// <summary>Uploads must be scanned in Production (NIST SI-3, #159); an engine needs a host.</summary>
    public static string? ValidateScanner(MediaScannerOptions scanner, bool isProduction)
    {
        if (scanner.Mode == MediaScannerMode.Disabled)
        {
            return isProduction
                ? "Media:Scanner:Mode=Disabled is not permitted in Production. Configure Mode=Icap (host, port, service path) " +
                  "or Mode=ClamAv so uploads are scanned for malware (NIST SI-3)."
                : null;
        }

        return string.IsNullOrWhiteSpace(scanner.Host)
            ? $"Media:Scanner:Host is required when Media:Scanner:Mode={scanner.Mode}."
            : null;
    }

    /// <summary>
    /// The relay's own consistency checks (port range, username/password pairing) plus: credentials never
    /// travel in clear text outside Development, so Security=None is refused there (Mailpit is a dev tool).
    /// </summary>
    public static string? ValidateSmtp(EmailOptions email, bool isDevelopment)
    {
        if (!email.IsEnabled) return null;

        try { email.Validate(); }
        catch (InvalidOperationException ex) { return ex.Message; }

        if (!isDevelopment && email.Smtp.Security == SmtpSecurity.None)
        {
            return "Email:Smtp:Security=None sends SMTP credentials and message bodies in clear text and is only permitted " +
                   "in Development. Use StartTls (port 587/25) or SslOnConnect (465) against the Exchange relay.";
        }

        return null;
    }

    /// <summary>
    /// Evaluates the DataAnnotations on an options instance. Program.cs also registers each class with
    /// ValidateDataAnnotations().ValidateOnStart(); running them here first puts the failures in the same
    /// numbered list as everything else instead of a later OptionsValidationException.
    /// </summary>
    public static IEnumerable<string> ValidateAnnotations(object options, string sectionName)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true))
            yield break;

        foreach (var r in results)
        {
            var members = string.Join(", ", r.MemberNames.Select(m => $"{sectionName}:{m}"));
            yield return members.Length == 0 ? $"{sectionName}: {r.ErrorMessage}" : $"{members}: {r.ErrorMessage}";
        }
    }
}
