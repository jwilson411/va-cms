namespace VA.CMS.API.GraphQL;

/// <summary>
/// Execution bounds for /api/graphql (#156). The endpoint is reachable
/// anonymously, so a request must not be able to fan out without limit.
/// </summary>
public static class GraphQLLimits
{
    /// <summary>Deepest selection set accepted; the schema's real relations need 3.</summary>
    public const int MaxExecutionDepth = 8;

    /// <summary>Hot Chocolate cost analysis ceilings (defaults are 10 000).</summary>
    public const double MaxFieldCost = 2_000;
    public const double MaxTypeCost  = 2_000;

    public static readonly TimeSpan ExecutionTimeout = TimeSpan.FromSeconds(10);
}
