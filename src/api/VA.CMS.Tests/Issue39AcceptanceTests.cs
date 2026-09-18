using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using VA.CMS.Infrastructure.Data.Pocos;
using VA.CMS.Infrastructure.Data.Repositories;
using VA.CMS.Infrastructure.Email;
using VA.CMS.Infrastructure.Outbox;
using VA.CMS.Infrastructure.Notifications;
using VA.CMS.Infrastructure.Settings;

namespace VA.CMS.Tests;

/// <summary>
/// Acceptance tests for issue #39 — Implement email notifications for workflow events via SMTP.
///
/// BRD FR-WORKFLOW-02.
///
/// Acceptance criteria:
///   AC1: Email fires on content submitted for review (to reviewers), returned (to author), published (to author)
///       => usp_Notification_CreateForWorkflowEvent (V040) returns each recipient with their address, including
///          the new ContentPublished event; the notifier queues one email per recipient after every transition.
///   AC2: Plain-language subject and body with a link to the content
///       => WorkflowEmailComposer: "Review requested: Title", the inbox description, next step, {AdminBaseUrl}/admin/content/{id}/edit.
///   AC3: SMTP configuration via environment variables
///       => the Email:Smtp section binds from Email__Smtp__Host, Email__Smtp__Port, ... and is validated at startup.
///          The switch, sender and link origin are site settings (notifications.emailEnabled / emailFromAddress /
///          emailFromName / adminBaseUrl) per the epic #141 rule, changeable without a deploy.
///   AC4: Supports Exchange on-prem and Exchange Online (TLS SMTP)
///       => SmtpEmailSender (MailKit) does STARTTLS / implicit TLS / none per Email:Smtp:Security and SMTP AUTH
///          when credentials are set; verified against an in-process SMTP server for the wire protocol.
/// </summary>
[Collection("Database")]
public class Issue39AcceptanceTests(DatabaseFixture fixture)
{
    private INotificationRepository Repo() => new NotificationRepository(fixture.CreateDb());

    // ── V040 structural ───────────────────────────────────────────────────────

    [Fact]
    public async Task V040_EventTypeCheck_AllowsContentPublished()
    {
        await using var conn = new SqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT [definition] FROM sys.check_constraints WHERE [name] = 'CK_Notification_EventType';";
        var definition = (string)(await cmd.ExecuteScalarAsync())!;
        Assert.Contains("ContentPublished", definition);
        Assert.Contains("ReviewRequested",  definition);
    }

    // ── AC1: recipients + addresses per event ─────────────────────────────────

    [Fact]
    public async Task AC1_ReviewRequested_ReturnsEveryReviewerWithTheirEmail()
    {
        var s = await Scenario.CreateAsync(fixture.ConnectionString, "hr/email-reviewers", "Email Page");

        var recipients = await Repo().CreateForWorkflowEventAsync(s.EntryId, NotificationEventTypes.ReviewRequested, s.AuthorId);

        var editor = Assert.Single(recipients, r => r.RecipientUserId == s.EditorId);
        Assert.Equal(s.EditorEmail, editor.RecipientEmail);
        Assert.Equal(s.EditorName,  editor.RecipientDisplayName);
        Assert.Equal("ReviewRequested", editor.EventType);
        Assert.Equal("Email Page", editor.ContentTitle);
        Assert.Equal($"{s.AuthorName} submitted \"Email Page\" for review", editor.Message);
        Assert.Equal(s.AuthorName, editor.ActorDisplayName);
        Assert.Equal(s.EntryId, editor.ContentEntryId);
        Assert.True(editor.Id > 0);
        Assert.DoesNotContain(recipients, r => r.RecipientUserId == s.AuthorId);
        Assert.All(recipients, r => Assert.False(string.IsNullOrWhiteSpace(r.RecipientEmail)));
    }

    [Fact]
    public async Task AC1_ContentReturned_GoesToAuthor_WithComment()
    {
        var s = await Scenario.CreateAsync(fixture.ConnectionString, "hr/email-returned", "Returned Page");

        var recipients = await Repo().CreateForWorkflowEventAsync(
            s.EntryId, NotificationEventTypes.ContentReturned, s.EditorId, "Fix the intro.");

        var author = Assert.Single(recipients);
        Assert.Equal(s.AuthorId,    author.RecipientUserId);
        Assert.Equal(s.AuthorEmail, author.RecipientEmail);
        Assert.Equal("Fix the intro.", author.Comment);
        Assert.Equal($"{s.EditorName} returned \"Returned Page\" to draft", author.Message);
    }

    [Fact]
    public async Task AC1_ContentPublished_GoesToAuthor_AndLandsInTheInbox()
    {
        var s = await Scenario.CreateAsync(fixture.ConnectionString, "hr/email-published", "Published Page");

        var recipients = await Repo().CreateForWorkflowEventAsync(s.EntryId, NotificationEventTypes.ContentPublished, s.EditorId);

        var author = Assert.Single(recipients);
        Assert.Equal(s.AuthorId, author.RecipientUserId);
        Assert.Equal("ContentPublished", author.EventType);
        Assert.Equal($"{s.EditorName} published \"Published Page\"", author.Message);

        var n = Assert.Single(await Repo().ListForUserAsync(s.AuthorId));
        Assert.Equal("ContentPublished", n.EventType);
        Assert.Equal(s.EditorId, n.ActorId);
        Assert.False(n.IsRead);
    }

    [Fact]
    public async Task AC1_ContentPublished_ByTheScheduler_HasNoActor_AndSaysSo()
    {
        var s = await Scenario.CreateAsync(fixture.ConnectionString, "hr/email-scheduled", "Scheduled Page");

        // ScheduledPublishWorker uses actor 0, which has no User row.
        var recipients = await Repo().CreateForWorkflowEventAsync(s.EntryId, NotificationEventTypes.ContentPublished, actorId: 0);

        var author = Assert.Single(recipients);
        Assert.Equal("\"Scheduled Page\" was published as scheduled", author.Message);
        Assert.Null(author.ActorDisplayName);

        var n = Assert.Single(await Repo().ListForUserAsync(s.AuthorId));
        Assert.Null(n.ActorId);
        Assert.Null(n.ActorDisplayName);
    }

    [Fact]
    public async Task AC1_ContentPublished_ByTheAuthor_NotifiesNobody()
    {
        var s = await Scenario.CreateAsync(fixture.ConnectionString, "hr/email-self-publish", "Self Page");

        Assert.Empty(await Repo().CreateForWorkflowEventAsync(s.EntryId, NotificationEventTypes.ContentPublished, s.AuthorId));
        Assert.Empty(await Repo().ListForUserAsync(s.AuthorId));
    }

    // ── AC2: subject, body, link ──────────────────────────────────────────────

    private const string AdminBaseUrl = "https://cms.example.gov/";
    private static readonly ISiteSettingsService Settings =
        StaticSiteSettings.Defaults.With(SiteSettingKeys.NotificationsAdminBaseUrl, AdminBaseUrl);

    private static NotificationRecipient Recipient(string eventType, string? comment = null) => new()
    {
        Id = 7, RecipientUserId = 42, RecipientEmail = "bob@va.gov", RecipientDisplayName = "Bob Reviewer",
        EventType = eventType, ContentEntryId = 123, ContentTitle = "Benefits Overview",
        Message = $"Alice Author {Verb(eventType)} \"Benefits Overview\"", ActorDisplayName = "Alice Author", Comment = comment,
    };

    private static string Verb(string eventType) => eventType switch
    {
        NotificationEventTypes.ReviewRequested  => "submitted",
        NotificationEventTypes.ContentReturned  => "returned",
        NotificationEventTypes.ContentApproved  => "approved",
        _                                       => "published",
    };

    [Theory]
    [InlineData(NotificationEventTypes.ReviewRequested,  "Review requested: Benefits Overview", "waiting for your review")]
    [InlineData(NotificationEventTypes.ContentReturned,  "Returned to draft: Benefits Overview", "submit it for review again")]
    [InlineData(NotificationEventTypes.ContentApproved,  "Approved: Benefits Overview",          "ready to publish")]
    [InlineData(NotificationEventTypes.ContentPublished, "Published: Benefits Overview",         "now live")]
    public void AC2_EveryEvent_HasPlainLanguageSubjectBody_AndLink(string eventType, string subject, string nextStep)
    {
        var email = WorkflowEmailComposer.Compose(Recipient(eventType), AdminBaseUrl);

        Assert.NotNull(email);
        Assert.Equal("bob@va.gov", email.ToAddress);
        Assert.Equal("Bob Reviewer", email.ToName);
        Assert.Equal(subject, email.Subject);
        Assert.StartsWith("Hi Bob Reviewer,", email.TextBody);
        Assert.Contains($"Alice Author {Verb(eventType)} \"Benefits Overview\"", email.TextBody);
        Assert.Contains(nextStep, email.TextBody);
        Assert.Contains("https://cms.example.gov/admin/content/123/edit", email.TextBody);
        Assert.NotNull(email.HtmlBody);
        Assert.Contains("<a href=\"https://cms.example.gov/admin/content/123/edit\">", email.HtmlBody);
        Assert.DoesNotContain("Reviewer's comment", email.TextBody);
    }

    [Fact]
    public void AC2_ReturnedEmail_CarriesTheReviewerComment_HtmlEscaped()
    {
        var email = WorkflowEmailComposer.Compose(
            Recipient(NotificationEventTypes.ContentReturned, comment: "Heading is <wrong> & too long"), AdminBaseUrl)!;

        Assert.Contains("Reviewer's comment:\nHeading is <wrong> & too long\n", email.TextBody);
        Assert.Contains("Heading is &lt;wrong&gt; &amp; too long", email.HtmlBody);
        Assert.DoesNotContain("<wrong>", email.HtmlBody);
    }

    [Fact]
    public void AC2_NoAdminBaseUrl_FallsBackToThePath()
    {
        var email = WorkflowEmailComposer.Compose(Recipient(NotificationEventTypes.ReviewRequested), adminBaseUrl: "")!;
        Assert.Contains("\n/admin/content/123/edit\n", email.TextBody);
    }

    [Fact]
    public void AC2_RecipientWithoutAddress_OrUnknownEvent_ProducesNoEmail()
    {
        var noAddress = Recipient(NotificationEventTypes.ReviewRequested);
        noAddress.RecipientEmail = " ";
        Assert.Null(WorkflowEmailComposer.Compose(noAddress, AdminBaseUrl));
        Assert.Null(WorkflowEmailComposer.Compose(Recipient("SomethingElse"), AdminBaseUrl));
    }

    // ── AC3: configuration via environment variables ──────────────────────────

    [Fact]
    public void AC3_EmailSection_BindsFromEnvironmentVariables()
    {
        var vars = new Dictionary<string, string?>
        {
            ["Email__Smtp__Host"]     = "smtp.office365.com",
            ["Email__Smtp__Port"]     = "587",
            ["Email__Smtp__Security"] = "StartTls",
            ["Email__Smtp__Username"] = "cms-noreply@va.gov",
            ["Email__Smtp__Password"] = "s3cret",
            ["Email__Smtp__TimeoutSeconds"] = "10",
        };
        foreach (var (k, v) in vars) Environment.SetEnvironmentVariable(k, v);
        try
        {
            var config  = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            var options = config.GetSection(EmailOptions.SectionName).Get<EmailOptions>()!;

            Assert.True(options.IsEnabled);
            Assert.Equal("smtp.office365.com",  options.Smtp.Host);
            Assert.Equal(587,                   options.Smtp.Port);
            Assert.Equal(SmtpSecurity.StartTls, options.Smtp.Security);
            Assert.Equal("cms-noreply@va.gov",  options.Smtp.Username);
            Assert.Equal("s3cret",              options.Smtp.Password);
            Assert.Equal(10,                    options.Smtp.TimeoutSeconds);
            options.Validate();   // fully configured: no throw
        }
        finally
        {
            foreach (var k in vars.Keys) Environment.SetEnvironmentVariable(k, null);
        }
    }

    [Fact]
    public void AC3_NoHost_MeansDisabled_AndValid()
    {
        var options = new EmailOptions();
        Assert.False(options.IsEnabled);
        Assert.Equal(587, options.Smtp.Port);
        Assert.Equal(SmtpSecurity.StartTls, options.Smtp.Security);
        options.Validate();
    }

    [Fact]
    public void AC3_HalfConfiguredRelay_FailsFast()
    {
        var halfAuth = new EmailOptions { Smtp = { Host = "smtp.va.gov", Username = "u" } };
        Assert.Contains("Password", Assert.Throws<InvalidOperationException>(halfAuth.Validate).Message);

        var badPort = new EmailOptions { Smtp = { Host = "smtp.va.gov", Port = 0 } };
        Assert.Contains("Port", Assert.Throws<InvalidOperationException>(badPort.Validate).Message);

        new EmailOptions { Smtp = { Host = "smtp.va.gov", Username = "u", Password = "p" } }.Validate();
    }

    [Fact]
    public void AC3_SenderAndSwitch_AreSiteSettings_WithDefaults()
    {
        var defaults = StaticSiteSettings.Defaults;
        Assert.True(defaults.GetBool(SiteSettingKeys.NotificationsEmailEnabled));
        Assert.Equal("cms-noreply@va.gov",    defaults.GetString(SiteSettingKeys.NotificationsEmailFromAddress));
        Assert.Equal("VA CMS",                defaults.GetString(SiteSettingKeys.NotificationsEmailFromName));
        Assert.Equal("http://localhost:5173", defaults.GetString(SiteSettingKeys.NotificationsAdminBaseUrl));

        var keys = new[]
        {
            SiteSettingKeys.NotificationsEmailEnabled, SiteSettingKeys.NotificationsEmailFromAddress,
            SiteSettingKeys.NotificationsEmailFromName, SiteSettingKeys.NotificationsAdminBaseUrl,
        };
        foreach (var key in keys)
        {
            var def = Assert.Single(SiteSettingDefinitions.All, d => d.Key == key);
            Assert.Equal(SiteSettingScope.Server, def.Scope);
            Assert.Equal(SiteSettingCategories.Notifications, def.Category);
        }
    }

    [Theory]
    [InlineData(SmtpSecurity.StartTls,     MailKit.Security.SecureSocketOptions.StartTls)]
    [InlineData(SmtpSecurity.SslOnConnect, MailKit.Security.SecureSocketOptions.SslOnConnect)]
    [InlineData(SmtpSecurity.Auto,         MailKit.Security.SecureSocketOptions.Auto)]
    [InlineData(SmtpSecurity.None,         MailKit.Security.SecureSocketOptions.None)]
    public void AC4_SecuritySetting_MapsOntoMailKit(SmtpSecurity security, MailKit.Security.SecureSocketOptions expected)
        => Assert.Equal(expected, SmtpEmailSender.ToSocketOptions(security));

    // ── Notifier: inbox rows → emails ─────────────────────────────────────────

    [Fact]
    public async Task Notifier_QueuesOneEmailPerRecipientWithAnAddress()
    {
        var repo = new Issue38NotificationStub();
        repo.Recipients.Add(new NotificationRecipient { Id = 1, RecipientUserId = 10, RecipientEmail = "ed@va.gov",  RecipientDisplayName = "Ed",  ContentTitle = "Page" });
        repo.Recipients.Add(new NotificationRecipient { Id = 2, RecipientUserId = 11, RecipientEmail = "",           RecipientDisplayName = "Nobody", ContentTitle = "Page" });
        repo.Recipients.Add(new NotificationRecipient { Id = 3, RecipientUserId = 12, RecipientEmail = "sam@va.gov", RecipientDisplayName = "Sam", ContentTitle = "Page" });
        var email = new CapturingEmailDispatcher();

        await Notifier(repo, email).NotifyAsync(5, NotificationEventTypes.ReviewRequested, actorId: 1);

        var batch = Assert.Single(email.Batches);
        Assert.Equal(["ed@va.gov", "sam@va.gov"], batch.Select(m => m.ToAddress).ToArray());
        Assert.All(batch, m => Assert.Equal("Review requested: Page", m.Subject));
        Assert.All(batch, m => Assert.Contains("https://cms.example.gov/admin/content/5/edit", m.TextBody));
    }

    [Fact]
    public async Task Notifier_NoRecipients_QueuesNothing()
    {
        var email = new CapturingEmailDispatcher();
        await Notifier(new Issue38NotificationStub(), email).NotifyAsync(5, NotificationEventTypes.ContentApproved, actorId: 1);
        Assert.Empty(email.Batches);
    }

    [Fact]
    public async Task Notifier_RepositoryFailure_SendsNoEmail_AndDoesNotThrow()
    {
        var email = new CapturingEmailDispatcher();
        await Notifier(new Issue38NotificationStub { ThrowOnCreate = true }, email)
            .NotifyAsync(5, NotificationEventTypes.ReviewRequested, actorId: 1);
        Assert.Empty(email.Batches);
    }

    [Fact]
    public async Task Notifier_DispatcherFailure_DoesNotThrow()
    {
        var repo = new Issue38NotificationStub();
        repo.Recipients.Add(new NotificationRecipient { Id = 1, RecipientUserId = 10, RecipientEmail = "ed@va.gov", RecipientDisplayName = "Ed", ContentTitle = "Page" });

        await Notifier(repo, new CapturingEmailDispatcher { ThrowOnEnqueue = true })
            .NotifyAsync(5, NotificationEventTypes.ReviewRequested, actorId: 1);
    }

    [Fact]
    public async Task Notifier_EmailSwitchOff_StillRecordsInbox_ButQueuesNoEmail()
    {
        var repo = new Issue38NotificationStub();
        repo.Recipients.Add(new NotificationRecipient { Id = 1, RecipientUserId = 10, RecipientEmail = "ed@va.gov", RecipientDisplayName = "Ed", ContentTitle = "Page" });
        var email = new CapturingEmailDispatcher();
        var off   = StaticSiteSettings.Defaults.With(SiteSettingKeys.NotificationsEmailEnabled, "false");

        await new WorkflowNotifier(repo, email, NullLogger<WorkflowNotifier>.Instance, off)
            .NotifyAsync(5, NotificationEventTypes.ReviewRequested, actorId: 1);

        Assert.Single(repo.Events);
        Assert.Empty(email.Batches);
    }

    [Fact]
    public async Task Notifier_FeatureNotificationsOff_RecordsNothing_AndQueuesNoEmail()
    {
        var repo = new Issue38NotificationStub();
        repo.Recipients.Add(new NotificationRecipient { Id = 1, RecipientUserId = 10, RecipientEmail = "ed@va.gov", RecipientDisplayName = "Ed", ContentTitle = "Page" });
        var email = new CapturingEmailDispatcher();
        var off   = StaticSiteSettings.Defaults.With(SiteSettingKeys.FeatureNotifications, "false");

        await new WorkflowNotifier(repo, email, NullLogger<WorkflowNotifier>.Instance, off)
            .NotifyAsync(5, NotificationEventTypes.ReviewRequested, actorId: 1);

        Assert.Empty(repo.Events);
        Assert.Empty(email.Batches);
    }

    private static WorkflowNotifier Notifier(INotificationRepository repo, IEmailDispatcher email)
        => new(repo, email, NullLogger<WorkflowNotifier>.Instance, Settings);

    // ── HTTP: every workflow action ends in an email ──────────────────────────

    [Fact]
    public async Task Http_SubmitReview_Return_Publish_EachQueueAnEmail()
    {
        var notifications = new Issue38NotificationStub();
        notifications.Recipients.Add(new NotificationRecipient
        {
            Id = 1, RecipientUserId = 10, RecipientEmail = "reviewer@va.gov", RecipientDisplayName = "Rae Reviewer", ContentTitle = "Page One",
        });
        var email = new CapturingEmailDispatcher();
        await using var factory = new Issue38TestFactory(notifications, initialStatus: "Draft", email);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice@va.gov");

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/content/1/submit-review", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/content/1/return", new { comment = "Tighten it up" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/content/1/publish", null)).StatusCode);
        // Re-publish of an already-published entry (no transition) still notifies.
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/content/1/publish", null)).StatusCode);

        Assert.Equal(
            ["ReviewRequested", "ContentReturned", "ContentPublished", "ContentPublished"],
            notifications.Events.Select(e => e.EventType).ToArray());

        Assert.Equal(4, email.Batches.Count);
        var subjects = email.Batches.Select(b => Assert.Single(b).Subject).ToArray();
        Assert.Equal(
            ["Review requested: Page One", "Returned to draft: Page One", "Published: Page One", "Published: Page One"],
            subjects);
        Assert.All(email.Batches, b => Assert.Equal("reviewer@va.gov", b[0].ToAddress));
        Assert.All(email.Batches, b => Assert.Contains("/admin/content/1/edit\n", b[0].TextBody));
        Assert.All(email.Batches, b => Assert.StartsWith("Hi Rae Reviewer,", b[0].TextBody));
        Assert.Contains("Tighten it up", email.Batches[1][0].TextBody);
    }

    [Fact]
    public async Task Http_RejectedTransition_SendsNoEmail()
    {
        var notifications = new Issue38NotificationStub();
        notifications.Recipients.Add(new NotificationRecipient { Id = 1, RecipientUserId = 10, RecipientEmail = "r@va.gov", RecipientDisplayName = "R", ContentTitle = "P" });
        var email = new CapturingEmailDispatcher();
        await using var factory = new Issue38TestFactory(notifications, initialStatus: "Draft", email);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Dev-User", "alice@va.gov");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await client.PostAsync("/api/v1/content/1/approve", null)).StatusCode);
        Assert.Empty(email.Batches);
    }

    // ── AC4: SmtpEmailSender on the wire ──────────────────────────────────────

    [Fact]
    public async Task AC4_SmtpSender_DeliversABatchOverOneConnection_WithAuth()
    {
        await using var server = await FakeSmtpServer.StartAsync(advertiseAuth: true);
        var sender = new SmtpEmailSender(new EmailOptions
        {
            Smtp = { Host = "127.0.0.1", Port = server.Port, Security = SmtpSecurity.None, Username = "svc", Password = "pw", TimeoutSeconds = 10 },
        }, StaticSiteSettings.Defaults
            .With(SiteSettingKeys.NotificationsEmailFromAddress, "cms-noreply@va.gov")
            .With(SiteSettingKeys.NotificationsEmailFromName,    "VA CMS"),
        NullLogger<SmtpEmailSender>.Instance);

        await sender.SendAsync(
        [
            new EmailMessage("ed@va.gov",  "Ed",  "Review requested: Page", "Hi Ed,\n\nhttps://cms.va.gov/admin/content/9/edit\n", "<p>Hi Ed</p>"),
            new EmailMessage("sam@va.gov", "Sam", "Review requested: Page", "Hi Sam,\n", null),
        ]);

        Assert.Equal(1, server.Connections);
        Assert.Equal(("svc", "pw"), server.Credentials);
        Assert.Equal(2, server.Messages.Count);

        Assert.Equal("cms-noreply@va.gov", server.Messages[0].From);
        Assert.Equal(["ed@va.gov"], server.Messages[0].To);
        Assert.Contains("Subject: Review requested: Page", server.Messages[0].Data);
        Assert.Contains("https://cms.va.gov/admin/content/9/edit", server.Messages[0].Data);
        Assert.Contains("text/html", server.Messages[0].Data);       // multipart/alternative with the HTML part
        Assert.Contains("From: VA CMS <cms-noreply@va.gov>", server.Messages[0].Data);

        Assert.Equal(["sam@va.gov"], server.Messages[1].To);
        Assert.DoesNotContain("text/html", server.Messages[1].Data);  // text-only message stays text/plain
        Assert.True(server.SawQuit);
    }

    [Fact]
    public async Task AC4_SmtpSender_SkipsARejectedRecipient_AndDeliversTheRest()
    {
        await using var server = await FakeSmtpServer.StartAsync(advertiseAuth: false, rejectRecipient: "gone@va.gov");
        var sender = new SmtpEmailSender(new EmailOptions
        {
            Smtp = { Host = "127.0.0.1", Port = server.Port, Security = SmtpSecurity.None, TimeoutSeconds = 10 },
        }, StaticSiteSettings.Defaults, NullLogger<SmtpEmailSender>.Instance);

        await sender.SendAsync(
        [
            new EmailMessage("gone@va.gov", null, "Published: Page", "gone\n"),
            new EmailMessage("here@va.gov", null, "Published: Page", "here\n"),
        ]);

        Assert.Null(server.Credentials);   // no credentials configured → no AUTH
        var delivered = Assert.Single(server.Messages);
        Assert.Equal(["here@va.gov"], delivered.To);
    }

    [Fact]
    public async Task AC4_SmtpSender_StartTlsRequired_FailsAgainstAServerWithoutIt()
    {
        // Exchange offers STARTTLS; a relay that does not must not silently get plaintext mail.
        await using var server = await FakeSmtpServer.StartAsync(advertiseAuth: false);
        var sender = new SmtpEmailSender(new EmailOptions
        {
            Smtp = { Host = "127.0.0.1", Port = server.Port, Security = SmtpSecurity.StartTls, TimeoutSeconds = 10 },
        }, StaticSiteSettings.Defaults, NullLogger<SmtpEmailSender>.Instance);

        await Assert.ThrowsAnyAsync<Exception>(() => sender.SendAsync([new EmailMessage("a@va.gov", null, "s", "b")]));
        Assert.Empty(server.Messages);
    }

    [Fact]
    public async Task OutboxDispatcher_QueuesToOutbox_AndConsumerDelivers_OrRetries()
    {
        // #171: Enqueue writes an outbox row; the consumer sends it later on whichever node claims it.
        var outbox     = new InMemoryOutboxRepository();
        var dispatcher = new OutboxEmailDispatcher(outbox, NullLogger<OutboxEmailDispatcher>.Instance);
        await dispatcher.EnqueueAsync([new EmailMessage("a@va.gov", null, "s", "b")]);
        var row = Assert.Single(outbox.Rows);
        Assert.Equal(OutboundEventTypes.Smtp, row.Type);

        IReadOnlyList<EmailMessage>? sent = null;
        var consumer = new OutboxEmailConsumer(new DelegateSender(m => { sent = m; return Task.CompletedTask; }),
            StaticSiteSettings.Defaults, NullLogger<OutboxEmailConsumer>.Instance);
        row.Attempts = 1;
        Assert.IsType<OutboxOutcome.SucceededOutcome>(await consumer.HandleAsync(row, CancellationToken.None));
        Assert.Equal("a@va.gov", Assert.Single(sent!).ToAddress);

        // A dead relay is a retry, not a lost message; the schedule follows notifications.emailRetryDelaysSeconds.
        var failing = new OutboxEmailConsumer(new DelegateSender(_ => throw new IOException("relay down")),
            StaticSiteSettings.Defaults, NullLogger<OutboxEmailConsumer>.Instance);
        var retry = Assert.IsType<OutboxOutcome.RetryOutcome>(await failing.HandleAsync(row, CancellationToken.None));
        Assert.Equal(TimeSpan.FromSeconds(30), retry.Delay);
        Assert.Contains("relay down", retry.Error);

        row.Attempts = 5;   // notifications.emailMaxAttempts default
        Assert.IsType<OutboxOutcome.FailedOutcome>(await failing.HandleAsync(row, CancellationToken.None));

        // An unavailable outbox must not surface to the workflow transition that already committed.
        var broken = new OutboxEmailDispatcher(new InMemoryOutboxRepository { ThrowOnEnqueue = true }, NullLogger<OutboxEmailDispatcher>.Instance);
        await broken.EnqueueAsync([new EmailMessage("a@va.gov", null, "s", "b")]);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private sealed class DelegateSender(Func<IReadOnlyList<EmailMessage>, Task> send) : IEmailSender
    {
        public Task SendAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken = default) => send(messages);
    }

    /// <summary>Author (ContentOwner on "hr/") + one global Editor, both with addresses, and one entry under "hr/".</summary>
    private sealed class Scenario
    {
        public long EntryId, AuthorId, EditorId;
        public string AuthorName = "", AuthorEmail = "", EditorName = "", EditorEmail = "";

        public static async Task<Scenario> CreateAsync(string connectionString, string slug, string title)
        {
            var s   = new Scenario();
            var tag = Guid.NewGuid().ToString("N")[..8];

            await using var conn = new SqlConnection(connectionString);
            await conn.OpenAsync();

            s.AuthorName  = $"zz39 Author {tag}";  s.AuthorEmail = $"issue39-author-{tag}@test.invalid";
            s.EditorName  = $"zz39 Editor {tag}";  s.EditorEmail = $"issue39-editor-{tag}@test.invalid";
            s.AuthorId    = await UserAsync(conn, $"issue39-author-{tag}", s.AuthorEmail, s.AuthorName);
            s.EditorId    = await UserAsync(conn, $"issue39-editor-{tag}", s.EditorEmail, s.EditorName);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    DECLARE @Hr BIGINT;
                    INSERT INTO [dbo].[ContentSection] ([Name],[SlugPrefix]) VALUES (@SectionName, 'hr/');
                    SET @Hr = SCOPE_IDENTITY();
                    INSERT INTO [dbo].[UserRole] ([UserId],[RoleId],[SectionId],[GrantedById])
                    SELECT @AuthorId, [Id], @Hr, @EditorId FROM [dbo].[Role] WHERE [Name] = 'ContentOwner';
                    INSERT INTO [dbo].[UserRole] ([UserId],[RoleId],[SectionId],[GrantedById])
                    SELECT @EditorId, [Id], NULL, @EditorId FROM [dbo].[Role] WHERE [Name] = 'Editor';

                    DECLARE @TypeId BIGINT = (SELECT TOP 1 [Id] FROM [dbo].[ContentType] WHERE [Name] = 'issue39_page');
                    IF @TypeId IS NULL
                    BEGIN
                        INSERT INTO [dbo].[ContentType] ([Name],[DisplayName],[FieldSchemaJson]) VALUES ('issue39_page','Issue 39 Page','[]');
                        SET @TypeId = SCOPE_IDENTITY();
                    END;
                    INSERT INTO [dbo].[ContentEntry] ([ContentTypeId],[Slug],[Locale],[Status],[OwnerId])
                    VALUES (@TypeId, @Slug, 'en-US', 'Draft', @AuthorId);
                    DECLARE @EntryId BIGINT = SCOPE_IDENTITY();
                    INSERT INTO [dbo].[ContentVersion] ([ContentEntryId],[VersionNumber],[FieldsJson],[Status],[AuthorId])
                    VALUES (@EntryId, 1, @FieldsJson, 'Draft', @AuthorId);
                    SELECT @EntryId;";
                cmd.Parameters.AddWithValue("@SectionName", $"HR {tag}");
                cmd.Parameters.AddWithValue("@AuthorId",    s.AuthorId);
                cmd.Parameters.AddWithValue("@EditorId",    s.EditorId);
                cmd.Parameters.AddWithValue("@Slug",        $"{slug}-{tag}");
                cmd.Parameters.AddWithValue("@FieldsJson",  System.Text.Json.JsonSerializer.Serialize(new { title }));
                s.EntryId = Convert.ToInt64(await cmd.ExecuteScalarAsync());
            }

            return s;
        }

        private static async Task<long> UserAsync(SqlConnection conn, string externalId, string email, string displayName)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO [dbo].[User] ([ExternalId],[Email],[DisplayName],[IsActive]) VALUES (@Ext, @Email, @Name, 1);
                SELECT SCOPE_IDENTITY();";
            cmd.Parameters.AddWithValue("@Ext",   externalId);
            cmd.Parameters.AddWithValue("@Email", email);
            cmd.Parameters.AddWithValue("@Name",  displayName);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync());
        }
    }
}

/// <summary>Synchronous stand-in for the background dispatcher: records every batch the notifier queues.</summary>
internal sealed class CapturingEmailDispatcher : IEmailDispatcher
{
    public List<IReadOnlyList<EmailMessage>> Batches { get; } = [];
    public bool ThrowOnEnqueue { get; init; }

    public Task EnqueueAsync(IReadOnlyList<EmailMessage> messages, CancellationToken ct = default)
    {
        if (ThrowOnEnqueue) throw new InvalidOperationException("mail queue unavailable");
        Batches.Add(messages);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Minimal in-process SMTP server (plaintext, one client at a time) that records what a
/// real client sends: EHLO, optional AUTH PLAIN, MAIL FROM / RCPT TO / DATA per message,
/// RSET after a rejected recipient, QUIT. Enough to prove SmtpEmailSender speaks SMTP.
/// </summary>
internal sealed class FakeSmtpServer : IAsyncDisposable
{
    public sealed record Message(string From, List<string> To, string Data);

    public int  Port        { get; private set; }
    public int  Connections { get; private set; }
    public bool SawQuit     { get; private set; }
    public (string User, string Password)? Credentials { get; private set; }
    public List<Message> Messages { get; } = [];

    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly bool _advertiseAuth;
    private readonly string? _rejectRecipient;
    private Task? _loop;
    private readonly CancellationTokenSource _cts = new();

    private FakeSmtpServer(bool advertiseAuth, string? rejectRecipient)
    {
        _advertiseAuth   = advertiseAuth;
        _rejectRecipient = rejectRecipient;
    }

    public static Task<FakeSmtpServer> StartAsync(bool advertiseAuth, string? rejectRecipient = null)
    {
        var server = new FakeSmtpServer(advertiseAuth, rejectRecipient);
        server._listener.Start();
        server.Port  = ((IPEndPoint)server._listener.LocalEndpoint).Port;
        server._loop = Task.Run(server.AcceptLoopAsync);
        return Task.FromResult(server);
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                Connections++;
                await ServeAsync(client);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII);
        using var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

        await writer.WriteLineAsync("220 fake.test ESMTP");

        string? from = null;
        var to = new List<string>();

        while (await reader.ReadLineAsync() is { } line)
        {
            var upper = line.ToUpperInvariant();
            if (upper.StartsWith("EHLO"))
            {
                await writer.WriteLineAsync("250-fake.test");
                if (_advertiseAuth) await writer.WriteLineAsync("250-AUTH PLAIN");
                await writer.WriteLineAsync("250 8BITMIME");
            }
            else if (upper.StartsWith("HELO"))
                await writer.WriteLineAsync("250 fake.test");
            else if (upper.StartsWith("AUTH PLAIN"))
            {
                var b64 = line.Length > "AUTH PLAIN".Length ? line["AUTH PLAIN".Length..].Trim() : null;
                if (b64 is null)
                {
                    await writer.WriteLineAsync("334 ");
                    b64 = (await reader.ReadLineAsync())?.Trim();
                }
                var parts = Encoding.UTF8.GetString(Convert.FromBase64String(b64 ?? "")).Split('\0');
                Credentials = (parts[^2], parts[^1]);
                await writer.WriteLineAsync("235 ok");
            }
            else if (upper.StartsWith("MAIL FROM:"))
            {
                from = Address(line);
                to.Clear();
                await writer.WriteLineAsync("250 ok");
            }
            else if (upper.StartsWith("RCPT TO:"))
            {
                var addr = Address(line);
                if (addr == _rejectRecipient)
                    await writer.WriteLineAsync("550 no such user");
                else
                {
                    to.Add(addr);
                    await writer.WriteLineAsync("250 ok");
                }
            }
            else if (upper == "DATA")
            {
                await writer.WriteLineAsync("354 go ahead");
                var data = new StringBuilder();
                while (await reader.ReadLineAsync() is { } dl && dl != ".")
                    data.Append(dl.StartsWith("..") ? dl[1..] : dl).Append("\r\n");
                Messages.Add(new Message(from ?? "", [.. to], data.ToString()));
                from = null; to.Clear();
                await writer.WriteLineAsync("250 queued");
            }
            else if (upper == "RSET" || upper == "NOOP")
            {
                from = null; to.Clear();
                await writer.WriteLineAsync("250 ok");
            }
            else if (upper == "QUIT")
            {
                SawQuit = true;
                await writer.WriteLineAsync("221 bye");
                return;
            }
            else if (upper == "STARTTLS")
                await writer.WriteLineAsync("454 TLS not available");
            else
                await writer.WriteLineAsync("500 unknown");
        }
    }

    private static string Address(string line)
    {
        var lt = line.IndexOf('<');
        var gt = line.IndexOf('>');
        return lt >= 0 && gt > lt ? line[(lt + 1)..gt] : line[(line.IndexOf(':') + 1)..].Trim();
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        if (_loop is not null)
        {
            try { await _loop; } catch { /* shutting down */ }
        }
        _cts.Dispose();
    }
}
