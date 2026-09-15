namespace VA.CMS.API.GraphQL.Types;

/// <summary>
/// Hot Chocolate GraphQL type for a navigation menu.
/// Maps to the NavigationMenu POCO / DB table.
/// </summary>
public class NavigationMenuType
{
    public long   Id        { get; init; }
    public string Name      { get; init; } = string.Empty;
    public string Handle    { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
