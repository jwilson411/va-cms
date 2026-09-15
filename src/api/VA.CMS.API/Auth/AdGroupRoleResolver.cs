using System.Security.Claims;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;

namespace VA.CMS.API.Auth;

/// <summary>
/// Resolves AD group membership from an identity's claims and merges the
/// resulting group-mapped roles with any explicitly-assigned roles.
///
/// AC (issue #67):
/// - At login/refresh, read the AD groups from the token's "groups" claim
///   (AAD emits group Object IDs or names depending on manifest setting).
///   In DevBypass mode the X-Dev-Groups header provides a comma-separated list.
/// - Apply the current AdGroupRoleMapping table to produce a set of group-sourced roles.
/// - Manually-assigned UserRole rows override group-mapped roles:
///   any explicit role wins. Explicit roles are the source of truth for
///   section-scoped assignments; group mappings are always global.
/// - Mapping changes take effect on the user's next login (not mid-session),
///   because roles are baked into the JWT at issuance time.
/// </summary>
public interface IAdGroupRoleResolver
{
    /// <summary>
    /// Given the AAD identity claims and the user's explicitly-assigned roles,
    /// return the merged effective role set.
    ///
    /// Merge rule:
    ///   effectiveRoles = explicitRoles ∪ groupMappedRoles
    ///   For any RoleName that appears in both, the explicit row wins (it may
    ///   carry a SectionId; the group-mapped row never does).
    /// </summary>
    Task<IEnumerable<UserRoleAssignment>> MergeRolesAsync(
        ClaimsPrincipal identity,
        IEnumerable<UserRoleAssignment> explicitRoles);

    /// <summary>
    /// Overload for DevBypass mode: group names come from the dev header,
    /// not from ClaimsPrincipal.
    /// </summary>
    Task<IEnumerable<UserRoleAssignment>> MergeRolesAsync(
        IEnumerable<string> adGroupNames,
        IEnumerable<UserRoleAssignment> explicitRoles);
}

/// <inheritdoc />
public sealed class AdGroupRoleResolver : IAdGroupRoleResolver
{
    private readonly IAdGroupMappingRepository _mappings;

    public AdGroupRoleResolver(IAdGroupMappingRepository mappings)
    {
        _mappings = mappings;
    }

    public async Task<IEnumerable<UserRoleAssignment>> MergeRolesAsync(
        ClaimsPrincipal identity,
        IEnumerable<UserRoleAssignment> explicitRoles)
    {
        // AAD emits group membership in "groups" claims (Object IDs or display names).
        // We match against the AdGroup column which stores the display name or
        // the OID as configured by the admin.
        var groupClaims = identity
            .FindAll("groups")
            .Select(c => c.Value)
            .ToList();

        return await MergeRolesAsync(groupClaims, explicitRoles);
    }

    public async Task<IEnumerable<UserRoleAssignment>> MergeRolesAsync(
        IEnumerable<string> adGroupNames,
        IEnumerable<UserRoleAssignment> explicitRoles)
    {
        var groupList    = adGroupNames.ToList();
        var explicitList = explicitRoles.ToList();

        if (groupList.Count == 0)
            return explicitList;

        // Resolve group-mapped roles from the database.
        var mapped = await _mappings.ResolveRolesForGroupsAsync(groupList);

        // Convert to UserRoleAssignment (global scope — group mappings are never section-scoped).
        var groupAssignments = mapped.Select(r => new UserRoleAssignment
        {
            RoleId   = r.RoleId,
            RoleName = r.RoleName,
            SectionId         = null,
            SectionSlugPrefix = null,
        }).ToList();

        if (groupAssignments.Count == 0)
            return explicitList;

        // Merge: explicit roles win for any RoleName collision.
        // Group mappings add only roles not already explicitly present globally.
        var explicitRoleNames = new HashSet<string>(
            explicitList.Select(r => r.RoleName),
            StringComparer.OrdinalIgnoreCase);

        var addFromGroups = groupAssignments
            .Where(g => !explicitRoleNames.Contains(g.RoleName))
            .DistinctBy(g => g.RoleName, StringComparer.OrdinalIgnoreCase);

        return explicitList.Concat(addFromGroups);
    }
}
