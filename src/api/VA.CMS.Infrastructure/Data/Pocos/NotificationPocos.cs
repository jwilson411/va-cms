namespace VA.CMS.Infrastructure.Data.Pocos;

/// <summary>
/// Workflow event types that produce an in-app notification and an email.
/// Issue #38 / #39 — BRD FR-WORKFLOW-02 / FR-WORKFLOW-03. Mirrors CK_Notification_EventType (V039, V040).
/// </summary>
public static class NotificationEventTypes
{
    /// <summary>Content was submitted for review → notify reviewers.</summary>
    public const string ReviewRequested = "ReviewRequested";
    /// <summary>A reviewer approved content → notify the author.</summary>
    public const string ContentApproved = "ContentApproved";
    /// <summary>A reviewer returned content to draft → notify the author.</summary>
    public const string ContentReturned = "ContentReturned";
    /// <summary>Content went live (direct, approved or scheduled publish) → notify the author. Issue #39.</summary>
    public const string ContentPublished = "ContentPublished";
}

/// <summary>
/// One notification created by usp_Notification_CreateForWorkflowEvent, with the recipient's
/// address so the same recipient set that fills the inbox also drives email (issue #39).
/// </summary>
public sealed class NotificationRecipient
{
    public long    Id                   { get; set; }
    public long    RecipientUserId      { get; set; }
    public string  RecipientEmail       { get; set; } = string.Empty;
    public string  RecipientDisplayName { get; set; } = string.Empty;
    public string  EventType            { get; set; } = string.Empty;
    public long    ContentEntryId       { get; set; }
    public string  ContentTitle         { get; set; } = string.Empty;
    /// <summary>Same plain-language description the inbox shows, e.g. <c>alice submitted "Page" for review</c>.</summary>
    public string  Message              { get; set; } = string.Empty;
    /// <summary>Null for the scheduled-publish system actor.</summary>
    public string? ActorDisplayName     { get; set; }
    public string? Comment              { get; set; }
}

/// <summary>
/// One row of the Notification table as returned by usp_Notification_ListForUser.
/// Issue #38 — in-app notification center.
/// </summary>
public sealed class Notification
{
    public long      Id              { get; set; }
    public long      RecipientUserId { get; set; }
    public string    EventType       { get; set; } = string.Empty;
    public long      ContentEntryId  { get; set; }
    public string    ContentTitle    { get; set; } = string.Empty;
    public string    Message         { get; set; } = string.Empty;
    public long?     ActorId         { get; set; }
    public string?   ActorDisplayName { get; set; }
    public string?   Comment         { get; set; }
    public bool      IsRead          { get; set; }
    public DateTime? ReadAt          { get; set; }
    public DateTime  CreatedAt       { get; set; }

    // Denormalised from ContentEntry so the panel can show current state without another call
    public string? EntrySlug   { get; set; }
    public string? EntryStatus { get; set; }
}
