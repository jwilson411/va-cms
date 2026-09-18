using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Webhooks;

/// <summary>
/// One-shot startup job (#168): any [Webhook].[Secret] still in clear text (rows written
/// before V046) is re-written as a Data Protection payload. Runs after migrations, in its
/// own scope, off the startup path (a BackgroundService, so a slow or unreachable database
/// never delays the host), and only logs on failure — the next start tries again and the
/// dispatcher accepts both forms meanwhile.
/// </summary>
public sealed class WebhookSecretRekeyService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IWebhookSecretProtector _protector;
    private readonly ILogger<WebhookSecretRekeyService> _logger;

    public WebhookSecretRekeyService(IServiceScopeFactory scopes, IWebhookSecretProtector protector, ILogger<WebhookSecretRekeyService> logger)
    {
        _scopes    = scopes;
        _protector = protector;
        _logger    = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var repo = scope.ServiceProvider.GetRequiredService<IWebhookRepository>();
            var rekeyed = await RekeyAsync(repo, _protector);
            if (rekeyed > 0)
                _logger.LogInformation("Re-keyed {Count} webhook secret(s) into Data Protection storage.", rekeyed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Webhook secret re-keying skipped; it will run again on the next start.");
        }
    }

    /// <summary>Protects every clear-text secret and returns how many rows changed.</summary>
    public static async Task<int> RekeyAsync(IWebhookRepository repo, IWebhookSecretProtector protector)
    {
        var count = 0;
        foreach (var (id, secret) in await repo.ListSecretsForRekeyAsync())
        {
            if (protector.IsProtected(secret)) continue;
            await repo.UpdateSecretAsync(id, protector.Protect(secret));
            count++;
        }
        return count;
    }
}
