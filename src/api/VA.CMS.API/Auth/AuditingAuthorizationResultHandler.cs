using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Auth;

/// <summary>
/// Writes one AuditLog row per authenticated request that a policy refuses (#165,
/// NIST AU-2 "unsuccessful access attempts"): who, which endpoint, which policy.
/// Anonymous 401s are not recorded here — they are noise from expired tokens and
/// the refresh endpoint audits the interesting ones itself. The default handler
/// still produces the 403 response.
/// </summary>
public sealed class AuditingAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    public const string Action = "AuthorizationDenied";

    private readonly AuthorizationMiddlewareResultHandler _default = new();
    private readonly ILogger<AuditingAuthorizationResultHandler> _logger;

    public AuditingAuthorizationResultHandler(ILogger<AuditingAuthorizationResultHandler> logger)
        => _logger = logger;

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && context.User.Identity?.IsAuthenticated == true)
            await WriteAsync(context, policy, authorizeResult);

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }

    private async Task WriteAsync(HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult result)
    {
        try
        {
            var audit = context.RequestServices.GetRequiredService<IAuditLogRepository>();
            _ = long.TryParse(context.User.FindFirst("cms_user_id")?.Value, out var actorId);

            var policyNames = context.GetEndpoint()?.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .Select(a => a.Policy)
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToArray() ?? [];
            var required = policy.Requirements
                .OfType<CmsRoleRequirement>()
                .SelectMany(r => r.AllowedRoles)
                .Distinct()
                .ToArray();
            var failed = result.AuthorizationFailure?.FailureReasons.Select(r => r.Message).ToArray() ?? [];

            var diff = JsonSerializer.Serialize(new
            {
                method = context.Request.Method,
                path   = context.Request.Path.Value,
                policy = policyNames,
                requiredRoles = required,
                reasons = failed,
            });

            await audit.WriteAsync(actorId == 0 ? null : actorId, "Endpoint", 0, Action, diff, AuditOutcome.Failure);
        }
        catch (Exception ex)
        {
            // Never let the audit write turn a 403 into a 500; the denial itself still stands.
            _logger.LogWarning(ex, "Could not write the AuthorizationDenied audit row for {Method} {Path}",
                context.Request.Method, context.Request.Path);
        }
    }
}
