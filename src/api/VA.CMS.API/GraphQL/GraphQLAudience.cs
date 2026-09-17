using HotChocolate;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Authorization;
using VA.CMS.API.Auth;

namespace VA.CMS.API.GraphQL;

/// <summary>
/// Decides which of the two GraphQL audiences a request belongs to (#156).
///
/// Anonymous callers see the same surface as the public REST API: Published
/// entries only, no owner/uploader identifiers, no media listing, and a single
/// media asset only when published content references it. Callers whose JWT
/// satisfies <see cref="CmsRoles.Policies.CanRead"/> get the full admin surface.
///
/// The principal is the <see cref="UserState"/> Hot Chocolate's HTTP transport
/// stores in the request context (the JWT bearer identity from UseAuthentication);
/// tests set it with <c>SetGlobalState(WellKnownContextData.UserState, new UserState(…))</c>.
/// </summary>
public interface IGraphQLAudience
{
    /// <summary>True when the caller may read Draft/InReview content and internal identifiers.</summary>
    Task<bool> CanReadAllAsync(IResolverContext context);
}

/// <inheritdoc />
public sealed class GraphQLAudience(IAuthorizationService authorization) : IGraphQLAudience
{
    private const string CacheKey = "vacms.graphql.canReadAll";

    public async Task<bool> CanReadAllAsync(IResolverContext context)
    {
        if (context.ContextData.TryGetValue(CacheKey, out var cached) && cached is bool b)
            return b;

        var user = context.ContextData.TryGetValue(WellKnownContextData.UserState, out var state)
            ? (state as UserState)?.User
            : null;
        var allowed = user?.Identity?.IsAuthenticated == true
            && (await authorization.AuthorizeAsync(user, CmsRoles.Policies.CanRead)).Succeeded;

        context.ContextData[CacheKey] = allowed;
        return allowed;
    }
}
