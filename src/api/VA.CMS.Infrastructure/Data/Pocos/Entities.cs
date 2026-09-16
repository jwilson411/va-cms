using PetaPoco;

namespace VA.CMS.Infrastructure.Data.Pocos;

[TableName("ContentType")]
[PrimaryKey("Id", AutoIncrement = true)]
public class ContentType
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? TemplateId { get; set; }
    public bool IsSystemType { get; set; }
    public bool AllowWorkflow { get; set; } = true;
    public string FieldSchemaJson { get; set; } = "[]";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

[TableName("ContentEntry")]
[PrimaryKey("Id", AutoIncrement = true)]
public class ContentEntry
{
    public long Id { get; set; }
    public long ContentTypeId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Locale { get; set; } = "en-US";
    public string Status { get; set; } = "Draft";
    public long? PublishedVersionId { get; set; }
    public DateTime? ScheduledPublishAt { get; set; }
    public DateTime? ScheduledExpireAt { get; set; }
    public long OwnerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// FieldsJson from the joined ContentVersion (published or latest).
    /// Populated by usp_ContentEntry_GetById — LEFT JOIN on PublishedVersionId.
    /// Null when no version exists yet.
    /// [Ignore]: PetaPoco must not include this in SP FetchAsync calls — set only by MapContentEntry.
    /// </summary>
    [PetaPoco.Ignore]
    public string? FieldsJson { get; set; }

    /// <summary>
    /// Pre-rendered HTML for RichText fields. Populated at publish time by Markdig pipeline.
    /// Null when entry has not been published or rendered.
    /// Issue #66: FR-AUTH-02a/02b.
    /// [Ignore]: PetaPoco must not include this in SP FetchAsync calls — set only by MapContentEntry.
    /// </summary>
    [PetaPoco.Ignore]
    public string? RenderedFieldsJson { get; set; }

    /// <summary>
    /// Content type machine name (ContentType.Name), from the LEFT JOIN in
    /// usp_ContentEntry_GetById. Null in queries that don't join ContentType.
    /// </summary>
    [PetaPoco.Ignore]
    public string? ContentTypeName { get; set; }
}

[TableName("ContentVersion")]
[PrimaryKey("Id", AutoIncrement = true)]
public class ContentVersion
{
    public long Id { get; set; }
    public long ContentEntryId { get; set; }
    public int VersionNumber { get; set; }
    public string FieldsJson { get; set; } = "{}";
    public string? RenderedFieldsJson { get; set; }
    public string Status { get; set; } = "Draft";
    public long AuthorId { get; set; }
    public string? ChangeNote { get; set; }
    public DateTime CreatedAt { get; set; }
}

[TableName("MediaAsset")]
[PrimaryKey("Id", AutoIncrement = true)]
public class MediaAsset
{
    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string StoragePath { get; set; } = string.Empty;
    public string StorageBackend { get; set; } = "local";
    public string MimeType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string? AltText { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public int? Width { get; set; }
    public int? Height { get; set; }
    public string? Tags { get; set; }
    public long UploadedById { get; set; }
    public bool? IsVirusScanPassed { get; set; }
    /// <summary>
    /// Storage path of the generated WebP variant. NULL for non-image assets or
    /// when WebP generation was skipped. Issue #41 — FR-MEDIA-02.
    /// </summary>
    public string? WebPStoragePath { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

[TableName("User")]
[PrimaryKey("Id", AutoIncrement = true)]
public class User
{
    public long Id { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

[TableName("NavigationMenu")]
[PrimaryKey("Id", AutoIncrement = true)]
public class NavigationMenu
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Handle { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

[TableName("AuditLog")]
[PrimaryKey("Id", AutoIncrement = true)]
public class AuditLog
{
    public long Id { get; set; }
    public long? ActorId { get; set; }
    public string? ActorEmail { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? DiffJson { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime CreatedAt { get; set; }
}
