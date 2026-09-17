using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Email;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #38 — Build in-app notification center for workflow events.
///
/// BRD FR-WORKFLOW-02 / FR-WORKFLOW-03.
///
/// Acceptance criteria:
///   AC1: Bell icon in admin top bar shows unread count badge
///       => GET /api/v1/notifications returns unreadCount; usp_Notification_UnreadCount counts only IsRead = 0.
///   AC2: Notification panel lists event description, content title, timestamp, link to content
///       => usp_Notification_ListForUser returns Message, ContentTitle, CreatedAt, ContentEntryId, newest first.
///   AC3: Reviewer is notified when content is submitted for their review
///       => ReviewRequested fans out to Editor/SiteAdmin/SystemAdmin (global or section-matching), never the actor.
///   AC4: Author is notified when content is approved or returned
///       => ContentApproved / ContentReturned go to the entry owner, with the return comment.
///   AC5: Notifications marked as read when viewed
///       => POST /api/v1/notifications/read marks own rows only; read-all clears the badge.
///
/// Migration: V039 adds the Notification table + 5 SPs.
/// </summary>
[Collection("Database")]
public class Issue38AcceptanceTests(DatabaseFixture fixture)
{
    private INotificationRepository Repo() => new NotificationRepository(fixture.CreateDb());

    // ── V039 structural ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("usp_Notification_CreateForWorkflowEvent")]
    [InlineData("usp_Notification_ListForUser")]
    [InlineData("usp_Notification_UnreadCount")]
    [InlineData("usp_Notification_MarkRead")]
    [InlineData("usp_Notification_MarkAllRead")]
    public async Task V039_StoredProcedure_Exists(string name)
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(1) FROM sys.objects WHERE [name] = @Name AND [type] = 'P';";
        cmd.Parameters.AddWithValue("@Name", name);
        Assert.Equal(1, (int)(await cmd.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task V039_NotificationTable_Exists_WithRecipientIndex()
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT COUNT(1)
            FROM   sys.indexes i
            JOIN   sys.tables  t ON t.[object_id] = i.[object_id]
            WHERE  t.[name] = 'Notification'
              AND  i.[name] = 'IX_Notification_Recipient_IsRead_CreatedAt';";
        Assert.Equal(1, (int)(await cmd.ExecuteScalarAsync())!);
    }

    // ── AC3: reviewers are notified on submit-review ──────────────────────────

    [Fact]
    public async Task AC3_ReviewRequested_NotifiesPublishCapableUsers_NotActorOrContentOwner()
    {
        var s = await Scenario.CreateAsync(fixture.ConnectionString, slug: "hr/benefits/page");

        var count = (await Repo().CreateForWorkflowEventAsync(s.EntryId, NotificationEventTypes.ReviewRequested, s.AuthorId)).Count;

        // At least: global Editor + global SystemAdmin + section-scoped Editor whose section
        // matches "hr/". Other tests in the shared DB seed more global reviewers, so the
        // per-user checks below are the real assertion.
        Assert.True(count >= 3, $"expected at least 3 recipients, got {count}");
        Assert.Single(await Repo().ListForUserAsync(s.GlobalEditorId));
        Assert.Single(await Repo().ListForUserAsync(s.GlobalSystemAdminId));
        Assert.Single(await Repo().ListForUserAsync(s.HrSectionEditorId));
        Assert.Empty(await Repo().ListForUserAsync(s.OtherSectionEditorId));   // scoped to "news/"
        Assert.Empty(await Repo().ListForUserAsync(s.AuthorId));               // ContentOwner, and the actor
        Assert.Empty(await Repo().ListForUserAsync(s.InactiveEditorId));       // IsActive = 0
    }

    [Fact]
    public async Task AC3_ReviewRequested_ActorWhoIsAlsoAReviewer_IsNotSelfNotified()
    {
        var s = await Scenario.CreateAsync(fixture.ConnectionString, slug: "hr/self-submit");

        // The global editor submits their own content: every other reviewer hears about it, they don't.
        var count = (await Repo().CreateForWorkflowEventAsync(s.EntryId, NotificationEventTypes.ReviewRequested, s.GlobalEditorId)).Count;

        Assert.True(count >= 2, $"expected at least 2 recipients, got {count}");
        Assert.Empty(await Repo().ListForUserAsync(s.GlobalEditorId));
        Assert.Single(await Repo().ListForUserAsync(s.GlobalSystemAdminId));
    }

    // ── AC2: panel fields ─────────────────────────────────────────────────────

    [Fact]
    public async Task AC2_Notification_CarriesDescription_Title_Timestamp_AndEntryLink()
    {
        var s = await Scenario.CreateAsync(fixture.ConnectionString, slug: "hr/panel-fields", title: "Benefits Overview");
        var before = DateTime.UtcNow.AddMinutes(-1);

        await Repo().CreateForWorkflowEventAsync(s.EntryId, NotificationEventTypes.ReviewRequested, s.AuthorId);
        var n = Assert.Single(await Repo().ListForUserAsync(s.GlobalEditorId));

        Assert.Equal("ReviewRequested", n.EventType);
        Assert.Equal("Benefits Overview", n.ContentTitle);
        Assert.Equal($"{s.AuthorName} submitted \"Benefits Overview\" for review", n.Message);
        Assert.Equal(s.EntryId, n.ContentEntryId);
        Assert.StartsWith("hr/panel-fields", n.EntrySlug);
        Assert.Equal(s.AuthorId, n.ActorId);
        Assert.Equal(s.AuthorName, n.ActorDisplayName);
        Assert.False(n.IsRead);
        Assert.Null(n.ReadAt);
        Assert.True(n.CreatedAt >= before);
    }

    [Fact]
    public async Task AC2_Notification_FallsBackToSlug_WhenVersionHasNoTitle()
    {
        var s = await Scenario.CreateAsync(fixture.ConnectionString, slug: "hr/no-title", title: null);

        await Repo().CreateForWorkflowEventAsync(s.EntryId, NotificationEventTypes.ReviewRequested, s.AuthorId);
        var n = Assert.Single(await Repo().ListForUserAsync(s.GlobalEditorId));

        Assert.StartsWith("hr/no-title", n.ContentTitle);   // slug fallback (tag-suffixed by Scenario)
    }

    [Fact]
    public async Task AC2_List_IsNewestFirst_AndHonoursLimitAndUnreadOnly()
    {
        var s = await Scenario.CreateAsync(fixture.ConnectionString, slug: "hr/ordering");
        var repo = Repo();

        await repo.CreateForWorkflowEventAsync(s.EntryId, NotificationEventTypes.ReviewRequested, s.AuthorId);
        await repo.CreateForWorkflowEventAsync(s.EntryId, NotificationEventTypes.ContentReturned, s.GlobalEditorId, "fix title");
        await repo.CreateForWorkflowEventAsync(s.EntryId, NotificationEventTypes.ContentApproved, s.GlobalEditorId);

        var all = await repo.ListForUserAsync(s.AuthorId);
        Assert.Equal(2, all.Count);   // author gets returned + approved, not the review request they sent
        Assert.Equal("ContentApproved", all[0].EventType);
        Assert.Equal("ContentReturned", all[1].EventType);

        Assert.Single(await repo.ListForUserAsync(s.AuthorId, limit: 1));

        await repo.MarkReadAsync(s.AuthorId, [all[0].Id]);
        var unread = await repo.ListForUserAsync(s.AuthorId, unreadOnly: true);
        Assert.Equal(all[1].Id, Assert.Single(unread).Id);
    }

    // ── AC4: author is notified on approve / return ───────────────────────────

    [Fact]
    public async Task AC4_ContentApproved_NotifiesOwnerOnly()
    {
        var s = await Scenario.CreateAsync(fixture.ConnectionString, slug: "hr/approved", title: "Approved Page");

        var recipients = await Repo().CreateForWorkflowEventAsync(s.EntryId, NotificationEventTypes.ContentApproved, s.GlobalEditorId);

        Assert.Equal(s.AuthorId, Assert.Single(recipients).RecipientUserId);
        var n = Assert.Single(await Repo().ListForUserAsync(s.AuthorId));
        Assert.Equal("ContentApproved", n.EventType);
        Assert.Equal($"{s.GlobalEditorName} approved \"Approved Page\"", n.Message);
        Assert.Null(n.Comment);
        Assert.Empty(await Repo().ListForUserAsync(s.GlobalSystemAdminId));
    }

    [Fact]
    public async Task AC4_ContentReturned_NotifiesOwner_WithReviewerComment()
    {
        var s = await Scenario.CreateAsync(fixture.ConnectionString, slug: "hr/returned", title: "Returned Page");

        var recipients = await Repo().CreateForWorkflowEventAsync(
            s.EntryId, NotificationEventTypes.ContentReturned, s.GlobalEditorId, "Please fix the heading.");

        Assert.Equal(s.AuthorId, Assert.Single(recipients).RecipientUserId);
        var n = Assert.Single(await Repo().ListForUserAsync(s.AuthorId));
        Assert.Equal("ContentReturned", n.EventType);
        Assert.Equal($"{s.GlobalEditorName} returned \"Returned Page\" to draft", n.Message);
        Assert.Equal("Please fix the heading.", n.Comment);
    }

    [Fact]
    public async Task AC4_OwnerActingOnOwnContent_IsNotSelfNotified()
    {
        var s = await Scenario.CreateAsync(fixture.ConnectionString, slug: "hr/self-approve");

        var recipients = await Repo().CreateForWorkflowEventAsync(s.EntryId, NotificationEventTypes.ContentApproved, s.AuthorId);

        Assert.Empty(recipients);
        Assert.Empty(await Repo().ListForUserAsync(s.AuthorId));
    }

    // ── AC1 / AC5: unread count and mark-read ─────────────────────────────────

    [Fact]
    public async Task AC1_AC5_UnreadCount_DropsAsNotificationsAreRead()
    {
        var s = await Scenario.CreateAsync(fixture.ConnectionString, slug: "hr/unread");
        var repo = Repo();

        await repo.CreateForWorkflowEventAsync(s.EntryId, NotificationEventTypes.ContentReturned, s.GlobalEditorId, "one");
        await repo.CreateForWorkflowEventAsync(s.EntryId, NotificationEventTypes.ContentReturned, s.GlobalEditorId, "two");
        await repo.CreateForWorkflowEventAsync(s.EntryId, NotificationEventTypes.ContentApproved, s.GlobalEditorId);
        Assert.Equal(3, await repo.UnreadCountAsync(s.AuthorId));

        var items = await repo.ListForUserAsync(s.AuthorId);
        Assert.Equal(1, await repo.MarkReadAsync(s.AuthorId, [items[0].Id]));
        Assert.Equal(2, await repo.UnreadCountAsync(s.AuthorId));

        // Marking the same id again is a no-op.
        Assert.Equal(0, await repo.MarkReadAsync(s.AuthorId, [items[0].Id]));

        Assert.Equal(2, await repo.MarkAllReadAsync(s.AuthorId));
        Assert.Equal(0, await repo.UnreadCountAsync(s.AuthorId));
        Assert.All(await repo.ListForUserAsync(s.AuthorId), n =>
        {
            Assert.True(n.IsRead);
            Assert.NotNull(n.ReadAt);
        });
    }

    [Fact]
    public async Task AC5_MarkRead_IgnoresOtherUsersNotifications()
    {
        var s = await Scenario.CreateAsync(fixture.ConnectionString, slug: "hr/mark-other");
        var repo = Repo();

        await repo.CreateForWorkflowEventAsync(s.EntryId, NotificationEventTypes.ContentApproved, s.GlobalEditorId);
        var authorsRow = Assert.Single(await repo.ListForUserAsync(s.AuthorId));

        // The editor tries to mark the author's row read.
        Assert.Equal(0, await repo.MarkReadAsync(s.GlobalEditorId, [authorsRow.Id]));
        Assert.Equal(1, await repo.UnreadCountAsync(s.AuthorId));

        // MarkRead with an empty list never touches the DB.
        Assert.Equal(0, await repo.MarkReadAsync(s.AuthorId, []));
    }

    // ── HTTP surface (stubbed repository) ─────────────────────────────────────

    [Fact]
    public async Task Http_SubmitReview_Approve_Return_RecordTheRightEvents()
    {
        var notifications = new Issue38NotificationStub();
        await using var factory = new Issue38TestFactory(notifications, initialStatus: "Draft");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice@va.gov");

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/content/1/submit-review", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/content/1/return", new { comment = "Needs work" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/content/1/submit-review", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/content/1/approve", null)).StatusCode);

        Assert.Equal(
            ["ReviewRequested", "ContentReturned", "ReviewRequested", "ContentApproved"],
            notifications.Events.Select(e => e.EventType).ToArray());
        Assert.All(notifications.Events, e => Assert.Equal(1L, e.EntryId));
        Assert.Equal("Needs work", notifications.Events[1].Comment);
        Assert.All(notifications.Events, e => Assert.True(e.ActorId > 0));
    }

    [Fact]
    public async Task Http_RejectedTransition_DoesNotNotify()
    {
        var notifications = new Issue38NotificationStub();
        await using var factory = new Issue38TestFactory(notifications, initialStatus: "Draft");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice@va.gov");

        // Draft → Approved is not an allowed edge.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync("/api/v1/content/1/approve", null)).StatusCode);
        Assert.Empty(notifications.Events);
    }

    [Fact]
    public async Task Http_NotifierFailure_DoesNotFailTheTransition()
    {
        var notifications = new Issue38NotificationStub { ThrowOnCreate = true };
        await using var factory = new Issue38TestFactory(notifications, initialStatus: "Draft");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice@va.gov");

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/content/1/submit-review", null)).StatusCode);
    }

    [Fact]
    public async Task Http_Inbox_List_MarkRead_MarkAllRead()
    {
        var notifications = new Issue38NotificationStub();
        await using var factory = new Issue38TestFactory(notifications, initialStatus: "Draft");
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice@va.gov");

        // Find alice's user id the same way the controller does: via the inbox call.
        // Seed three rows for her by first discovering her id from an empty list call.
        var probe = await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
        Assert.Equal(0, probe.GetProperty("unreadCount").GetInt32());
        var aliceId = notifications.LastListedUserId!.Value;

        notifications.Seed(aliceId, "ContentApproved", "Page A");
        notifications.Seed(aliceId, "ContentReturned", "Page B", comment: "Typo");
        notifications.Seed(aliceId + 1, "ContentApproved", "Someone else's");

        var list = await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications");
        Assert.Equal(2, list.GetProperty("unreadCount").GetInt32());
        var items = list.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("Page B", items[0].GetProperty("contentTitle").GetString());   // newest first
        Assert.Equal("Typo",   items[0].GetProperty("comment").GetString());
        Assert.False(items[0].GetProperty("isRead").GetBoolean());
        Assert.True(items[0].GetProperty("contentEntryId").GetInt64() > 0);
        Assert.True(items[0].TryGetProperty("createdAt", out _));

        var firstId = items[0].GetProperty("id").GetInt64();
        var read = await client.PostAsJsonAsync("/api/v1/notifications/read", new { ids = new[] { firstId } });
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(1, (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("updated").GetInt32());

        var count = await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count");
        Assert.Equal(1, count.GetProperty("unreadCount").GetInt32());

        var empty = await client.PostAsJsonAsync("/api/v1/notifications/read", new { ids = Array.Empty<long>() });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var readAll = await client.PostAsync("/api/v1/notifications/read-all", null);
        Assert.Equal(HttpStatusCode.OK, readAll.StatusCode);
        var after = await client.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count");
        Assert.Equal(0, after.GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task Http_Inbox_RequiresAuthentication()
    {
        await using var factory = new Issue38TestFactory(new Issue38NotificationStub(), initialStatus: "Draft");
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var res = await client.GetAsync("/api/v1/notifications");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    // ── DB scenario ───────────────────────────────────────────────────────────

    /// <summary>
    /// One content entry under "hr/" owned by a ContentOwner, plus a spread of reviewers:
    /// global Editor, global SystemAdmin, Editor scoped to "hr/", Editor scoped to "news/",
    /// and an inactive global Editor. Every run gets fresh users so tests never share inboxes.
    /// </summary>
    private sealed class Scenario
    {
        public long EntryId;
        public long AuthorId;              public string AuthorName       = string.Empty;
        public long GlobalEditorId;        public string GlobalEditorName = string.Empty;
        public long GlobalSystemAdminId;
        public long HrSectionEditorId;
        public long OtherSectionEditorId;
        public long InactiveEditorId;

        public static async Task<Scenario> CreateAsync(string connectionString, string slug, string? title = "Test Page")
        {
            var s   = new Scenario();
            var tag = Guid.NewGuid().ToString("N")[..8];
            slug    = $"{slug}-{tag}";

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            // Display names sort after the "User <guid>" rows other tests seed, so this
            // scenario's users never push them off usp_User_List's first page.
            s.AuthorName       = $"zz38 Author {tag}";
            s.GlobalEditorName = $"zz38 Editor {tag}";
            s.AuthorId             = await UserAsync(conn, tag, "author",   s.AuthorName);
            s.GlobalEditorId       = await UserAsync(conn, tag, "editor",   s.GlobalEditorName);
            s.GlobalSystemAdminId  = await UserAsync(conn, tag, "sysadmin", $"zz38 SysAdmin {tag}");
            s.HrSectionEditorId    = await UserAsync(conn, tag, "hr-ed",    $"zz38 HR Editor {tag}");
            s.OtherSectionEditorId = await UserAsync(conn, tag, "news-ed",  $"zz38 News Editor {tag}");
            s.InactiveEditorId     = await UserAsync(conn, tag, "inactive", $"zz38 Inactive {tag}", isActive: false);

            var hrSection   = await SectionAsync(conn, $"HR {tag}",   "hr/");
            var newsSection = await SectionAsync(conn, $"News {tag}", "news/");

            await RoleAsync(conn, s.AuthorId,             "ContentOwner", hrSection, s.GlobalSystemAdminId);
            await RoleAsync(conn, s.GlobalEditorId,       "Editor",       null,        s.GlobalSystemAdminId);
            await RoleAsync(conn, s.GlobalSystemAdminId,  "SystemAdmin",  null,        s.GlobalSystemAdminId);
            await RoleAsync(conn, s.HrSectionEditorId,    "Editor",       hrSection,   s.GlobalSystemAdminId);
            await RoleAsync(conn, s.OtherSectionEditorId, "Editor",       newsSection, s.GlobalSystemAdminId);
            await RoleAsync(conn, s.InactiveEditorId,     "Editor",       null,        s.GlobalSystemAdminId);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    DECLARE @TypeId BIGINT = (SELECT TOP 1 [Id] FROM [dbo].[ContentType] WHERE [Name] = 'issue38_page');
                    IF @TypeId IS NULL
                    BEGIN
                        INSERT INTO [dbo].[ContentType] ([Name],[DisplayName],[FieldSchemaJson]) VALUES ('issue38_page','Issue 38 Page','[]');
                        SET @TypeId = SCOPE_IDENTITY();
                    END;
                    INSERT INTO [dbo].[ContentEntry] ([ContentTypeId],[Slug],[Locale],[Status],[OwnerId])
                    VALUES (@TypeId, @Slug, 'en-US', 'Draft', @OwnerId);
                    DECLARE @EntryId BIGINT = SCOPE_IDENTITY();
                    INSERT INTO [dbo].[ContentVersion] ([ContentEntryId],[VersionNumber],[FieldsJson],[Status],[AuthorId])
                    VALUES (@EntryId, 1, @FieldsJson, 'Draft', @OwnerId);
                    SELECT @EntryId;";
                cmd.Parameters.AddWithValue("@Slug",       slug);
                cmd.Parameters.AddWithValue("@OwnerId",    s.AuthorId);
                cmd.Parameters.AddWithValue("@FieldsJson", title is null ? "{}" : JsonSerializer.Serialize(new { title }));
                s.EntryId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }

            return s;
        }

        private static async Task<long> UserAsync(SqlConnection conn, string tag, string key, string displayName, bool isActive = true)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO [dbo].[User] ([ExternalId],[Email],[DisplayName],[IsActive])
                VALUES (@Ext, @Email, @Name, @Active);
                SELECT SCOPE_IDENTITY();";
            cmd.Parameters.AddWithValue("@Ext",    $"issue38-{key}-{tag}");
            cmd.Parameters.AddWithValue("@Email",  $"issue38-{key}-{tag}@test.invalid");
            cmd.Parameters.AddWithValue("@Name",   displayName);
            cmd.Parameters.AddWithValue("@Active", isActive);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        private static async Task<long> SectionAsync(SqlConnection conn, string name, string prefix)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT INTO [dbo].[ContentSection] ([Name],[SlugPrefix]) VALUES (@Name, @Prefix); SELECT SCOPE_IDENTITY();";
            cmd.Parameters.AddWithValue("@Name",   name);
            cmd.Parameters.AddWithValue("@Prefix", prefix);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }

        private static async Task RoleAsync(SqlConnection conn, long userId, string role, long? sectionId, long grantedBy)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO [dbo].[UserRole] ([UserId],[RoleId],[SectionId],[GrantedById])
                SELECT @UserId, [Id], @SectionId, @GrantedBy FROM [dbo].[Role] WHERE [Name] = @Role;";
            cmd.Parameters.AddWithValue("@UserId",    userId);
            cmd.Parameters.AddWithValue("@Role",      role);
            cmd.Parameters.AddWithValue("@SectionId", (object?)sectionId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@GrantedBy", grantedBy);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}

// ── Stubs / host ─────────────────────────────────────────────────────────────

/// <summary>
/// In-memory notification repository. Records CreateForWorkflowEvent calls and keeps a
/// per-user inbox so the NotificationsController can be exercised without a database.
/// Also used by the other workflow test factories so a successful transition never
/// reaches for a real connection string.
/// </summary>
internal sealed class Issue38NotificationStub : INotificationRepository
{
    public List<(long EntryId, string EventType, long ActorId, string? Comment)> Events { get; } = [];
    public bool  ThrowOnCreate    { get; init; }
    public long? LastListedUserId { get; private set; }

    /// <summary>
    /// Recipients every CreateForWorkflowEvent call reports back (issue #39): the notifier
    /// turns each into an email. Empty by default, as for an entry nobody else reviews.
    /// </summary>
    public List<NotificationRecipient> Recipients { get; } = [];

    private readonly List<Notification> _rows = [];
    private long _nextId = 1;

    public void Seed(long recipientId, string eventType, string title, string? comment = null)
    {
        _rows.Add(new Notification
        {
            Id = _nextId++, RecipientUserId = recipientId, EventType = eventType, ContentEntryId = 1,
            ContentTitle = title, Message = $"Someone {eventType} \"{title}\"", Comment = comment,
            CreatedAt = DateTime.UtcNow.AddSeconds(_nextId),
        });
    }

    public Task<IReadOnlyList<NotificationRecipient>> CreateForWorkflowEventAsync(long contentEntryId, string eventType, long actorId, string? comment = null)
    {
        if (ThrowOnCreate) throw new InvalidOperationException("notification store unavailable");
        Events.Add((contentEntryId, eventType, actorId, comment));
        IReadOnlyList<NotificationRecipient> result = Recipients
            .Select(r => new NotificationRecipient
            {
                Id = r.Id, RecipientUserId = r.RecipientUserId, RecipientEmail = r.RecipientEmail,
                RecipientDisplayName = r.RecipientDisplayName, EventType = eventType, ContentEntryId = contentEntryId,
                ContentTitle = r.ContentTitle, Message = $"Someone {eventType} \"{r.ContentTitle}\"",
                ActorDisplayName = "Someone", Comment = comment,
            })
            .ToList();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<Notification>> ListForUserAsync(long userId, bool unreadOnly = false, int limit = 50)
    {
        LastListedUserId = userId;
        IReadOnlyList<Notification> result = _rows
            .Where(n => n.RecipientUserId == userId && (!unreadOnly || !n.IsRead))
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(limit)
            .ToList();
        return Task.FromResult(result);
    }

    public Task<int> UnreadCountAsync(long userId)
        => Task.FromResult(_rows.Count(n => n.RecipientUserId == userId && !n.IsRead));

    public Task<int> MarkReadAsync(long userId, IReadOnlyCollection<long> ids)
    {
        var updated = 0;
        foreach (var n in _rows.Where(n => n.RecipientUserId == userId && !n.IsRead && ids.Contains(n.Id)))
        {
            n.IsRead = true; n.ReadAt = DateTime.UtcNow; updated++;
        }
        return Task.FromResult(updated);
    }

    public Task<int> MarkAllReadAsync(long userId)
        => MarkReadAsync(userId, _rows.Where(n => n.RecipientUserId == userId).Select(n => n.Id).ToList());
}

internal sealed class Issue38TestFactory : WebApplicationFactory<Program>
{
    private readonly Issue38NotificationStub _notifications;
    private readonly WorkflowTransitionTests.WorkflowEntryStub _entries;
    private readonly IEmailDispatcher? _email;

    /// <param name="email">Issue #39: replaces the background email dispatcher so tests can see what the notifier queued.</param>
    public Issue38TestFactory(Issue38NotificationStub notifications, string initialStatus, IEmailDispatcher? email = null)
    {
        _notifications = notifications;
        _entries       = new WorkflowTransitionTests.WorkflowEntryStub(initialStatus);   // one instance: status carries across requests
        _email         = email;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("SKIP_MIGRATIONS",   "true");
        builder.UseSetting("Auth:Mode",         "DevBypass");
        builder.UseSetting("Auth:DevBypassAllowedUsers:0", "alice@va.gov");
        builder.UseSetting("Jwt:SigningKey",    "issue-38-notification-tests-key32!");
        builder.UseSetting("Jwt:Issuer",        "va-cms-api");
        builder.UseSetting("Jwt:Audience",      "va-cms-spa");
        builder.UseSetting("AzureAd:Instance",     "https://login.microsoftonline.com/");
        builder.UseSetting("AzureAd:TenantId",     "00000000-0000-0000-0000-000000000001");
        builder.UseSetting("AzureAd:ClientId",     "00000000-0000-0000-0000-000000000002");
        builder.UseSetting("AzureAd:ClientSecret", "test-secret");
        builder.UseSetting("AzureAd:CallbackPath", "/api/auth/callback");
        builder.UseSetting("ConnectionStrings:DefaultConnection",
            "Server=localhost,14333;Database=VACMS_Dev;User Id=sa;Password=VaCms_Dev!2026;TrustServerCertificate=True;Connection Timeout=5;");

        builder.ConfigureServices(services =>
        {
            Replace<IUserRepository>(services,              _ => new Issue68UserStub());
            Replace<IDbMonitorRepository>(services,         _ => new Issue68DbMonitorStub());
            Replace<IContentEntryRepository>(services,      _ => _entries);
            Replace<IContentVersionRepository>(services,    _ => new Issue23ContentVersionStub());
            Replace<IContentTypeRepository>(services,       _ => new Issue23ContentTypeStub());
            Replace<IMediaAltTextGuardRepository>(services, _ => new NoMissingAltTextStub());
            Replace<INotificationRepository>(services,      _ => _notifications);
            if (_email is not null)
                Replace<IEmailDispatcher>(services,         _ => _email);
        });
    }

    private static void Replace<T>(IServiceCollection services, Func<IServiceProvider, T> factory) where T : class
    {
        var existing = services.SingleOrDefault(d => d.ServiceType == typeof(T));
        if (existing != null) services.Remove(existing);
        services.AddScoped<T>(factory);
    }
}
