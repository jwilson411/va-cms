using VA.CMS.Infrastructure.Data.Pocos;

namespace VA.CMS.Infrastructure.Data.Repositories;

/// <summary>
/// Repository for the AdGroupRoleMapping table.
/// All DB access via EXEC usp_AdGroupMapping_* stored procedures.
/// </summary>
public interface IAdGroupMappingRepository
{
    /// <summary>Returns all mappings (for admin display), including role name.</summary>
    Task<IEnumerable<AdGroupRoleMappingRow>> ListAsync();

    /// <summary>
    /// Upserts a (AdGroup, RoleId) mapping.
    /// Returns the Id of the inserted or existing row.
    /// </summary>
    Task<long> UpsertAsync(string adGroup, long roleId, long createdById);

    /// <summary>Deletes a mapping by its Id. No-op if the Id does not exist.</summary>
    Task DeleteAsync(long id);

    /// <summary>
    /// Resolves a set of AD group names to CMS role names.
    /// Used at login/refresh time. Returns distinct role names matched by group membership.
    /// </summary>
    Task<IEnumerable<(long RoleId, string RoleName)>> ResolveRolesForGroupsAsync(
        IEnumerable<string> adGroups);
}
