using PetaPoco;

namespace VA.CMS.Infrastructure.Data.Pocos;

[TableName("Role")]
[PrimaryKey("Id", AutoIncrement = true)]
public class Role
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

[TableName("UserRole")]
[PrimaryKey("Id", AutoIncrement = true)]
public class UserRole
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long RoleId { get; set; }
    public long? SectionId { get; set; }
    public long GrantedById { get; set; }
    public DateTime CreatedAt { get; set; }
}

[TableName("WorkflowTransition")]
[PrimaryKey("Id", AutoIncrement = true)]
public class WorkflowTransition
{
    public long Id { get; set; }
    public long ContentEntryId { get; set; }
    public long ContentVersionId { get; set; }
    public string FromStatus { get; set; } = string.Empty;
    public string ToStatus { get; set; } = string.Empty;
    public long ActorId { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; set; }
}

[TableName("NavigationItem")]
[PrimaryKey("Id", AutoIncrement = true)]
public class NavigationItem
{
    public long Id { get; set; }
    public long MenuId { get; set; }
    public long? ParentItemId { get; set; }
    public string Label { get; set; } = string.Empty;
    public string? Url { get; set; }
    public long? ContentEntryId { get; set; }
    public string Target { get; set; } = "_self";
    public int SortOrder { get; set; }
    public bool IsVisible { get; set; } = true;
    public int Depth { get; set; }
}

[TableName("Redirect")]
[PrimaryKey("Id", AutoIncrement = true)]
public class Redirect
{
    public long Id { get; set; }
    public string FromPath { get; set; } = string.Empty;
    public string ToPath { get; set; } = string.Empty;
    public int StatusCode { get; set; } = 301;
    public bool IsActive { get; set; } = true;
    public long CreatedById { get; set; }
    public DateTime CreatedAt { get; set; }
}

[TableName("Taxonomy")]
[PrimaryKey("Id", AutoIncrement = true)]
public class Taxonomy
{
    public long Id { get; set; }
    public string Handle { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

[TableName("TaxonomyTerm")]
[PrimaryKey("Id", AutoIncrement = true)]
public class TaxonomyTerm
{
    public long Id { get; set; }
    public long TaxonomyId { get; set; }
    public long? ParentTermId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public int Depth { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

[TableName("Webhook")]
[PrimaryKey("Id", AutoIncrement = true)]
public class Webhook
{
    public long Id { get; set; }
    public string Url { get; set; } = string.Empty;
    public string? Secret { get; set; }
    public string EventsJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

[TableName("WebhookDelivery")]
[PrimaryKey("Id", AutoIncrement = true)]
public class WebhookDelivery
{
    public long Id { get; set; }
    public long WebhookId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = "{}";
    public int? ResponseStatusCode { get; set; }
    public int AttemptNumber { get; set; } = 1;
    public DateTime? DeliveredAt { get; set; }
    public string? ErrorMessage { get; set; }
}

[TableName("MediaUsage")]
[PrimaryKey("Id", AutoIncrement = true)]
public class MediaUsage
{
    public long Id { get; set; }
    public long MediaAssetId { get; set; }
    public long ContentEntryId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Extended MediaUsage row returned by usp_MediaAsset_GetUsage —
/// joins ContentEntry to expose Slug, Status, ContentTypeId.
/// Issue #42: usage list in media library detail panel.
/// </summary>
public class MediaUsageDetail
{
    public long   ContentEntryId { get; set; }
    public string FieldName      { get; set; } = string.Empty;
    public string Slug           { get; set; } = string.Empty;
    public string Status         { get; set; } = string.Empty;
    public long   ContentTypeId  { get; set; }
}

/// <summary>Result row from usp_Search_FullText.</summary>
public class SearchResult
{
    public long Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public long ContentTypeId { get; set; }
    public string Locale { get; set; } = "en-US";
    public DateTime UpdatedAt { get; set; }
    public string? Excerpt { get; set; }
    public int Rank { get; set; }
}

/// <summary>Result row from usp_User_GetRoles.</summary>
public class UserRoleAssignment
{
    public long RoleId { get; set; }
    public string RoleName { get; set; } = string.Empty;
    public long? SectionId { get; set; }
    public string? SectionSlugPrefix { get; set; }
}
