using PetaPoco;

namespace VA.CMS.Infrastructure.Data.Pocos;

/// <summary>
/// Row in the AdGroupRoleMapping table.
/// Represents a mapping from an AD group name to a CMS Role.
/// </summary>
[TableName("AdGroupRoleMapping")]
[PrimaryKey("Id", AutoIncrement = true)]
public class AdGroupRoleMapping
{
    public long Id { get; set; }

    /// <summary>AD group display name, e.g. "VA-CMS-Editors".</summary>
    public string AdGroup { get; set; } = string.Empty;

    public long RoleId { get; set; }

    public long CreatedById { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Projection returned by usp_AdGroupMapping_List — joins in the role name.
/// </summary>
public class AdGroupRoleMappingRow
{
    public long Id { get; set; }
    public string AdGroup { get; set; } = string.Empty;
    public long RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public long CreatedById { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
