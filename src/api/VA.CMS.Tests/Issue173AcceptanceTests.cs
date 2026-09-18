using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VA.CMS.API;
using VA.CMS.API.Auth;
using VA.CMS.API.Controllers.Admin;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Email;
using VA.CMS.Infrastructure.Settings;
using VA.CMS.Infrastructure.Storage;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #173: fail-fast startup validation.
///   - Each rule in StartupValidation has its own unit test.
///   - A misconfigured host reports every problem in one numbered list.
///   - Options classes are bound through the options pipeline with ValidateOnStart.
/// </summary>
public class Issue173AcceptanceTests
{
    private const string GoodKey = "issue-173-acceptance-key-32chars!";

    // ── signing key ───────────────────────────────────────────────────────────

    [Fact]
    public void SigningKey_Empty_Is_Only_Tolerated_In_Development()
    {
        Assert.Null(StartupValidation.ValidateSigningKey("", isDevelopment: true));
        Assert.Contains("required", StartupValidation.ValidateSigningKey("", isDevelopment: false)!);
        Assert.Contains("required", StartupValidation.ValidateSigningKey(null, isDevelopment: false)!);
    }

    [Theory]
    [InlineData("REPLACE_WITH_32_CHAR_OR_LONGER_SECRET")]          // appsettings.Development.json.example
    [InlineData("<set-via-environment-variable-or-secrets>")]      // appsettings.WindowsAuth.json.example
    [InlineData("this-is-an-example-key-that-is-long-enough!!")]
    public void SigningKey_Placeholder_Is_Refused_Even_In_Development(string key)
    {
        Assert.Contains("placeholder", StartupValidation.ValidateSigningKey(key, isDevelopment: true)!);
        Assert.Contains("placeholder", StartupValidation.ValidateSigningKey(key, isDevelopment: false)!);
    }

    [Fact]
    public void SigningKey_Must_Be_At_Least_32_Bytes()
    {
        Assert.Contains("32 bytes", StartupValidation.ValidateSigningKey("only-ten-c", isDevelopment: false)!);
        Assert.Contains("32 bytes", StartupValidation.ValidateSigningKey(new string('x', 31) , isDevelopment: false)!);
        Assert.Null(StartupValidation.ValidateSigningKey(GoodKey, isDevelopment: false));
        // Bytes, not characters: 16 two-byte characters are 32 bytes.
        Assert.Null(StartupValidation.ValidateSigningKey("ñáéíóúàèìòùâêîôû", isDevelopment: false));
    }

    [Fact]
    public void SigningKey_With_No_Entropy_Is_Refused()
    {
        Assert.Contains("entropy", StartupValidation.ValidateSigningKey(new string('a', 64), isDevelopment: false)!);
        Assert.Contains("entropy", StartupValidation.ValidateSigningKey("abababababababababababababababababab", isDevelopment: false)!);
    }

    // ── connection string ─────────────────────────────────────────────────────

    [Fact]
    public void ConnectionString_Must_Be_Present()
    {
        Assert.NotNull(StartupValidation.ValidateConnectionStringPresent(null));
        Assert.NotNull(StartupValidation.ValidateConnectionStringPresent("  "));
        Assert.Null(StartupValidation.ValidateConnectionStringPresent("Server=sql;Database=VACMS;"));
    }

    [Theory]
    [InlineData("Server=sql;Database=VACMS;User Id=vacms_app;Password=x;TrustServerCertificate=True;", "TrustServerCertificate")]
    [InlineData("Server=sql;Database=VACMS;User Id=vacms_app;Password=x;Encrypt=False;",              "Encrypt=False")]
    [InlineData("Server=sql;Database=VACMS;User Id=sa;Password=x;Encrypt=True;",                     "User Id=sa")]
    public void ConnectionString_Security_Is_Enforced_In_Production_Only(string connectionString, string expectedFault)
    {
        Assert.Contains(expectedFault, StartupValidation.ValidateConnectionStringSecurity(connectionString, isProduction: true)!);
        Assert.Null(StartupValidation.ValidateConnectionStringSecurity(connectionString, isProduction: false));
    }

    [Fact]
    public void ConnectionString_Reports_Every_Fault_At_Once()
    {
        var msg = StartupValidation.ValidateConnectionStringSecurity(
            "Server=sql;Database=VACMS;User Id=sa;Password=x;Encrypt=False;TrustServerCertificate=True;", isProduction: true)!;
        Assert.Contains("TrustServerCertificate", msg);
        Assert.Contains("Encrypt=False", msg);
        Assert.Contains("User Id=sa", msg);
    }

    [Fact]
    public void ConnectionString_Compliant_Production_Value_Passes()
    {
        Assert.Null(StartupValidation.ValidateConnectionStringSecurity(
            "Server=sql.va.gov;Database=VACMS;User Id=vacms_app;Password=x;Encrypt=True;TrustServerCertificate=False;", isProduction: true));
        Assert.Null(StartupValidation.ValidateConnectionStringSecurity(
            "Server=sql.va.gov;Database=VACMS;Integrated Security=True;Encrypt=Strict;", isProduction: true));
    }

    // ── auth mode ─────────────────────────────────────────────────────────────

    [Fact]
    public void DevBypass_And_Fake_Handlers_Are_Development_Only()
    {
        var devBypass = new AuthOptions { Mode = AuthMode.DevBypass, DevBypassAllowedUsers = ["alice@va.gov"] };
        var empty     = new ConfigurationBuilder().Build();

        Assert.Null(StartupValidation.ValidateAuthMode(devBypass, empty, isDevelopment: true));
        Assert.Contains("DevBypass is only permitted", StartupValidation.ValidateAuthMode(devBypass, empty, isDevelopment: false)!);

        var noUsers = new AuthOptions { Mode = AuthMode.DevBypass };
        Assert.Contains("DevBypassAllowedUsers", StartupValidation.ValidateAuthMode(noUsers, empty, isDevelopment: true)!);

        var fakes = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WINDOWS_AUTH_FAKE_NEGOTIATE"] = "true",
            ["AZUREAD_FAKE_OIDC"]           = "true",
        }).Build();
        var windows = new AuthOptions { Mode = AuthMode.WindowsAuth };
        Assert.Null(StartupValidation.ValidateAuthMode(windows, fakes, isDevelopment: true));
        var msg = StartupValidation.ValidateAuthMode(windows, fakes, isDevelopment: false)!;
        Assert.Contains("WINDOWS_AUTH_FAKE_NEGOTIATE", msg);
        Assert.Contains("AZUREAD_FAKE_OIDC", msg);
    }

    [Fact]
    public void AzureAd_CallbackPath_Must_Not_Shadow_Api_Routes()
    {
        Assert.Null(StartupValidation.ValidateAzureAdCallbackPath(AuthMode.AzureAd, null));
        Assert.Null(StartupValidation.ValidateAzureAdCallbackPath(AuthMode.AzureAd, "/signin-oidc"));
        Assert.Contains("collides", StartupValidation.ValidateAzureAdCallbackPath(AuthMode.AzureAd, "/api/auth/callback")!);
        Assert.Null(StartupValidation.ValidateAzureAdCallbackPath(AuthMode.WindowsAuth, "/api/auth/callback"));   // not in use
    }

    // ── scanner / storage / smtp ──────────────────────────────────────────────

    [Fact]
    public void Disabled_Scanner_Is_Refused_In_Production_And_Engines_Need_A_Host()
    {
        var disabled = new MediaScannerOptions { Mode = MediaScannerMode.Disabled };
        Assert.Null(StartupValidation.ValidateScanner(disabled, isProduction: false));
        Assert.Contains("Media:Scanner:Mode=Disabled", StartupValidation.ValidateScanner(disabled, isProduction: true)!);

        var noHost = new MediaScannerOptions { Mode = MediaScannerMode.Icap, Host = " " };
        Assert.Contains("Media:Scanner:Host", StartupValidation.ValidateScanner(noHost, isProduction: false)!);
        Assert.Null(StartupValidation.ValidateScanner(new MediaScannerOptions { Mode = MediaScannerMode.ClamAv, Host = "clamav" }, isProduction: true));
    }

    [Fact]
    public void Storage_Backend_Rules_Are_Part_Of_The_List()
    {
        var problems = StartupValidation.Evaluate(Config(new() { ["Storage:Backend"] = "azure_blob" }), "Staging");
        Assert.Contains(problems, p => p.Contains("Storage:Backend 'azure_blob' is not supported"));
    }

    [Fact]
    public void Smtp_Security_None_Is_Development_Only()
    {
        var plain = new EmailOptions { Smtp = new SmtpOptions { Host = "mailpit", Port = 1025, Security = SmtpSecurity.None } };
        Assert.Null(StartupValidation.ValidateSmtp(plain, isDevelopment: true));
        Assert.Contains("Email:Smtp:Security=None", StartupValidation.ValidateSmtp(plain, isDevelopment: false)!);

        var tls = new EmailOptions { Smtp = new SmtpOptions { Host = "mail.va.gov", Port = 587, Security = SmtpSecurity.StartTls } };
        Assert.Null(StartupValidation.ValidateSmtp(tls, isDevelopment: false));

        var halfAuth = new EmailOptions { Smtp = new SmtpOptions { Host = "mail.va.gov", Username = "svc" } };
        Assert.Contains("Password", StartupValidation.ValidateSmtp(halfAuth, isDevelopment: false)!);

        Assert.Null(StartupValidation.ValidateSmtp(new EmailOptions(), isDevelopment: false));   // log-only delivery
    }

    // ── runtime settings policy ───────────────────────────────────────────────

    [Fact]
    public void AdminBaseUrl_Must_Be_Https_Outside_Development()
    {
        var def = SiteSettingDefinitions.Get(SiteSettingKeys.NotificationsAdminBaseUrl);

        Assert.Null(SiteSettingsController.ValidatePolicy(def, "http://localhost:5173", isDevelopment: true));
        Assert.Contains("https://", SiteSettingsController.ValidatePolicy(def, "http://cms-admin.va.gov", isDevelopment: false)!);
        Assert.Null(SiteSettingsController.ValidatePolicy(def, "https://cms-admin.va.gov", isDevelopment: false));
        Assert.Contains("absolute", SiteSettingsController.ValidatePolicy(def, "cms-admin.va.gov", isDevelopment: false)!);
        Assert.Contains("absolute", SiteSettingsController.ValidatePolicy(def, "ftp://x", isDevelopment: true)!);

        // Other keys are untouched by the policy layer.
        Assert.Null(SiteSettingsController.ValidatePolicy(SiteSettingDefinitions.Get(SiteSettingKeys.SiteTitle), "anything", isDevelopment: false));
    }

    // ── DataAnnotations ───────────────────────────────────────────────────────

    [Fact]
    public void Annotations_On_Options_Classes_Are_Reported_With_Their_Section()
    {
        var scanner = new MediaScannerOptions { Mode = MediaScannerMode.Icap, Host = "av", Port = 70000, TimeoutSeconds = 0 };
        var msgs = StartupValidation.ValidateAnnotations(scanner, MediaScannerOptions.SectionName).ToList();
        Assert.Contains(msgs, m => m.StartsWith("Media:Scanner:Port:"));
        Assert.Contains(msgs, m => m.StartsWith("Media:Scanner:TimeoutSeconds:"));

        var smtp = new SmtpOptions { Port = 0 };
        Assert.Contains(StartupValidation.ValidateAnnotations(smtp, "Email:Smtp"), m => m.StartsWith("Email:Smtp:Port:"));

        Assert.Empty(StartupValidation.ValidateAnnotations(new JwtOptions { SigningKey = GoodKey }, "Jwt"));
        Assert.Contains(StartupValidation.ValidateAnnotations(new JwtOptions { Issuer = "" }, "Jwt"), m => m.StartsWith("Jwt:Issuer:"));
    }

    // ── the whole list ────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_Returns_Every_Problem_Not_Just_The_First()
    {
        var config = Config(new()
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=sql;Database=VACMS;User Id=sa;Password=x;TrustServerCertificate=True;",
            ["Jwt:SigningKey"]                      = "short",
            ["Auth:Mode"]                           = "DevBypass",
            ["AllowedHosts"]                        = "*",
            ["Storage:Backend"]                     = "azure_blob",
            ["Media:Scanner:Mode"]                  = "Disabled",
            ["Email:Smtp:Host"]                     = "relay",
            ["Email:Smtp:Security"]                 = "None",
        });

        var problems = StartupValidation.Evaluate(config, "Production");

        Assert.True(problems.Count >= 7, string.Join("\n", problems));
        Assert.Contains(problems, p => p.Contains("TrustServerCertificate"));
        Assert.Contains(problems, p => p.Contains("Jwt:SigningKey"));
        Assert.Contains(problems, p => p.Contains("DevBypass"));
        Assert.Contains(problems, p => p.Contains("AllowedHosts"));
        Assert.Contains(problems, p => p.Contains("azure_blob"));
        Assert.Contains(problems, p => p.Contains("Media:Scanner:Mode=Disabled"));
        Assert.Contains(problems, p => p.Contains("Email:Smtp:Security=None"));
    }

    [Fact]
    public void Evaluate_Is_Quiet_For_A_Compliant_Production_Host()
    {
        var config = Config(new()
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=sql.va.gov;Database=VACMS;User Id=vacms_app;Password=x;Encrypt=True;TrustServerCertificate=False;",
            ["Jwt:SigningKey"]                      = GoodKey,
            ["Auth:Mode"]                           = "WindowsAuth",
            ["AllowedHosts"]                        = "cms.va.gov",
            ["Storage:Backend"]                     = "unc",
            ["Storage:UncRootPath"]                 = @"\\files\va-cms",
            ["Media:Scanner:Mode"]                  = "Icap",
            ["Media:Scanner:Host"]                  = "avscan.va.gov",
            ["DataProtection:KeysPath"]             = @"\\files\va-cms\dp-keys",   // #168
        });

        Assert.Empty(StartupValidation.Evaluate(config, "Production"));
    }

    [Fact]
    public void Evaluate_Is_Quiet_For_The_Development_Defaults()
    {
        // Empty signing key, wildcard hosts, disabled scanner, SA + TrustServerCertificate: all fine locally.
        var config = Config(new()
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=x;TrustServerCertificate=True;",
            ["Auth:Mode"]                           = "DevBypass",
            ["Auth:DevBypassAllowedUsers:0"]        = "alice@va.gov",
            ["AllowedHosts"]                        = "*",
        });

        Assert.Empty(StartupValidation.Evaluate(config, "Development"));
    }

    [Fact]
    public void Host_Refuses_To_Start_With_A_Numbered_List()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            using var factory = new MisconfiguredFactory();
            factory.CreateClient();
        });

        Assert.Contains("configuration problems (ASPNETCORE_ENVIRONMENT=Staging)", ex.Message);
        var numbered = Regex.Matches(ex.Message, @"^\s+\d+\. ", RegexOptions.Multiline);
        Assert.True(numbered.Count >= 3, ex.Message);
        Assert.Contains("1. ", ex.Message);
        Assert.Contains("Jwt:SigningKey", ex.Message);
        Assert.Contains("AllowedHosts", ex.Message);
        Assert.Contains("Email:Smtp:Security=None", ex.Message);
    }

    [Fact]
    public void Options_Are_Available_Through_The_Options_Pipeline()
    {
        using var factory = new GoodFactory();
        factory.CreateClient();

        Assert.Equal(GoodKey, factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value.SigningKey);
        Assert.Same(factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value, factory.Services.GetRequiredService<JwtOptions>());
        Assert.Equal("local", factory.Services.GetRequiredService<IOptions<StorageOptions>>().Value.Backend);
        Assert.Equal(MediaScannerMode.Disabled, factory.Services.GetRequiredService<IOptions<MediaScannerOptions>>().Value.Mode);
        Assert.Equal(AuthMode.WindowsAuth, factory.Services.GetRequiredService<IOptions<AuthOptions>>().Value.Mode);
        Assert.False(factory.Services.GetRequiredService<IOptions<EmailOptions>>().Value.IsEnabled);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static IConfiguration Config(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed class MisconfiguredFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Staging");
            builder.UseSetting("SKIP_MIGRATIONS", "true");
            builder.UseSetting("AllowedHosts", "*");
            builder.UseSetting("Auth:Mode", "AzureAd");
            builder.UseSetting("Jwt:SigningKey", "REPLACE_WITH_32_CHAR_OR_LONGER_SECRET");
            builder.UseSetting("Email:Smtp:Host", "relay");
            builder.UseSetting("Email:Smtp:Security", "None");
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=5;");
        }
    }

    private sealed class GoodFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("SKIP_MIGRATIONS", "true");
            builder.UseSetting("Auth:Mode", "WindowsAuth");
            builder.UseSetting("WINDOWS_AUTH_FAKE_NEGOTIATE", "true");
            builder.UseSetting("Jwt:SigningKey", GoodKey);
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=5;");
            builder.ConfigureServices(services =>
            {
                foreach (var d in services.Where(d => d.ServiceType == typeof(IDbMonitorRepository)).ToList())
                    services.Remove(d);
                services.AddScoped<IDbMonitorRepository>(_ => new AuthTestStubs.StubDbMonitorRepository());
                AuthTestStubs.UseInMemoryAuth(services);
            });
        }
    }
}
