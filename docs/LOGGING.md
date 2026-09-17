# Logging and Monitoring Policy

**Scope:** the VA CMS API (`VA.CMS.API`). The admin SPA and the public site log nothing server-side beyond
what Next.js / IIS write on their own. Issue #166 (epic #152); BRD NFR-OPS-01 / NFR-OPS-02; NIST AU-2, AU-3,
AU-9, AU-11.

This document is the operator-facing description of **what the API logs, at which level, where it goes,
what must never appear in it, and how long it is kept**. The audit trail (who changed what, AU-2/AU-3) is
a separate, database-backed record described in `DATABASE_LAYER.md` §4.9 — application logs are for
operating the system, the audit log is for accountability. They share one correlation id.

## 1. Pipeline

Serilog, structured, one JSON object per event (`Serilog.Formatting.Compact`). Every event carries:

| Field | Source | Notes |
|---|---|---|
| `@t`, `@l`, `@mt`, `@x` | Serilog | timestamp (UTC), level, message template, exception |
| `Application` | constant | `va-cms-api` |
| `Environment` | `ASPNETCORE_ENVIRONMENT` | `Development`, `Staging`, `Production` |
| `MachineName` | host | which node in the farm |
| `SourceContext` | logger category | the C# type that wrote the line |
| `CorrelationId` | `CorrelationIdMiddleware` | per request; also in `AuditLog.CorrelationId`, the `X-Correlation-Id` response header and every ProblemDetails body |
| `RequestId` | ASP.NET | Kestrel's connection-scoped id (same value as the generated correlation id when no inbound header) |
| `UserId` | request-completion line | CMS user id (`cms_user_id` claim), never the UPN |
| `ClientIp` | request-completion line | after `ForwardedHeaders` trust, i.e. the real client behind IIS ARR |

Levels are read from the standard `Logging:LogLevel` section (`Default`, per-namespace overrides;
`None` silences a namespace). Sinks are configured under `Logging:Sinks`:

```
Logging__LogLevel__Default=Information
Logging__LogLevel__Microsoft.AspNetCore=Warning

Logging__Sinks__Console__Enabled=true                 # stdout; IIS captures it via stdoutLogEnabled, or leave it for Development
Logging__Sinks__Console__Format=Json                  # Json (default outside Development) | Text

Logging__Sinks__File__Enabled=true                    # rolling JSON file, one per day, size-capped
Logging__Sinks__File__Path=D:\logs\vacms\api-.json    # → api-20260917.json, api-20260917_001.json on overflow
Logging__Sinks__File__RetainedFileCountLimit=31
Logging__Sinks__File__FileSizeLimitBytes=104857600

Logging__Sinks__EventLog__Enabled=true                # Windows Event Log → Application, source "VA CMS API"
Logging__Sinks__EventLog__MinimumLevel=Warning        # operators, not request traces
                                                      # the deployment script creates the source (needs admin once):
                                                      #   New-EventLog -LogName Application -Source "VA CMS API"

Logging__Sinks__Splunk__Enabled=true                  # HTTP Event Collector on the on-prem Splunk
Logging__Sinks__Splunk__HecUrl=https://splunk-hec.va.gov:8088
Logging__Sinks__Splunk__Token=<hec token>             # secret — injected by the deployment tool (DEPLOYMENT.md "Secrets on the host")
Logging__Sinks__Splunk__Index=vacms
Logging__Sinks__Splunk__SourceType=_json
```

Any combination may be on at once. `Logging:Sinks` is validated at startup (#173): a file sink without a
path, a Splunk sink without a token or with an `http://` collector outside Development, or the Event Log
sink on a non-Windows host all stop the API with a numbered list of problems. Nothing goes to a cloud
service.

## 2. What is logged, at which level

| Level | Used for | Examples |
|---|---|---|
| **Critical/Fatal** | the process cannot continue | startup validation failures, database behind the migration set |
| **Error** | a request or job failed and someone should look | unhandled exception (with correlation id), storage backend failure on upload, SMTP rejection, webhook dispatch failure, scheduled publish failure |
| **Warning** | degraded but handled | settings snapshot could not refresh (defaults in effect), CSP violation report, virus scanner unavailable, refresh-token replay detected, forbidden request (403) |
| **Information** | normal operation, one line per unit of work | request completed (`HTTP GET /api/v1/content/x responded 200 in 3.2 ms`), session issued for user id, migration status, email sent (masked recipient), scheduled publish executed |
| **Debug** | developer diagnostics; off outside Development | liveness/readiness polls, settings refresh ticks, DevBypass token issuance |

Rules of thumb for contributors:

- One `Information` line per request comes from `UseSerilogRequestLogging`; controllers do not add a second
  "handling request" line.
- Log **ids**, not entities: `EntryId`, `UserId`, `AssetId`, `WebhookId`. The audit log has the before/after.
- Log the exception object (`_logger.LogError(ex, …)`), not `ex.Message` in the template, so the stack and
  inner exceptions are structured.
- Clients never see `ex.Message`: unhandled exceptions become an RFC 7807 ProblemDetails body with only the
  status and the `correlationId`; the detail is in the log under that id. In Development the body also
  carries `detail` and `exception` for the developer.

## 3. PII and secrets — never in a log line

VA Handbook 6500 data-minimization applies to logs as to any other store. The API does **not** write:

| Never logged | Instead |
|---|---|
| UPN / e-mail / display name of a signed-in user | `UserId` (the `User` table maps it back; access to that table is itself audited) |
| Recipient addresses of workflow e-mails | masked, `a***@va.gov` (`PiiMask.Email`) |
| `Authorization` header / bearer tokens / refresh cookies | `AuthHeaderRedactionMiddleware` replaces the value; request logging never logs headers |
| Passwords, connection strings, SMTP/Splunk/HEC tokens | startup validation reports the *name* of a bad setting, not its value |
| Content bodies, field values, uploaded file contents | ids and byte counts only |
| Search query text | goes to `SearchQueryLog` (analytics), not the application log — see #175 for its own retention |
| Full request URLs with query strings containing tokens | the preview-token endpoint logs the entry id, not the token |

Failed logons log the *reason* and the client IP at Warning; the identifier that failed is in the audit
row (AU-2), not the log line. The DevBypass path (Development only) logs the UPN — it never runs outside a
developer's machine.

## 4. Retention and protection (AU-9, AU-11)

Log files and forwarded events are Federal records. Retention follows the **VA Records Control Schedule
(RCS 10-1)** item for system and security log files as designated by the system's ISSO in the System
Security Plan; the API does not implement retention itself beyond the file sink's rolling limit, which is a
disk-space guard, not the records policy. Practically:

- **Splunk** (preferred): the index's retention policy is set by the Splunk administrators to the
  RCS-designated period; the API's own files can then be short-lived (`RetainedFileCountLimit=31`).
- **File sink without Splunk**: the log directory must be on a volume that the backup job covers, ACL'd to
  the app-pool identity (write) and operators (read); rotate to the archive location per the RCS period.
- **Event Log**: subject to the host's Event Log size and the enterprise Windows Event Forwarding policy.

Logs are write-only for the application (the app-pool identity has no delete right on the archive) and
readable by the operations and security teams only. Splunk access uses the enterprise role model; the
`vacms` index must not be readable by content editors.

## 5. Health endpoints (NFR-OPS-01)

| Endpoint | Purpose | Body |
|---|---|---|
| `GET /health`, `GET /health/live` | liveness: the process serves requests; no dependencies | `{"status":"Healthy"}` |
| `GET /health/ready` | readiness: SQL Server (`SELECT 1` as the app login), storage root writable (probe file through the configured backend), settings snapshot loaded and fresh, SMTP relay reachable when e-mail is on | `{"status":"Healthy"|"Degraded"|"Unhealthy"}`; 503 when Unhealthy |

The same three routes exist under `/api/health` for hosts that route only `/api/*` to the API. Both are
anonymous so monitoring tools need no token, but the readiness body carries per-check names, durations and
failure text **only** for a caller with the Developer role (`Authorization: Bearer …`). A Degraded result
(e.g. the SMTP relay is down, or `notifications.adminBaseUrl` is still the `http://localhost` default in
Staging) returns 200 so the node stays in the load balancer while the operators see the reason.

Point the load balancer at `/health/live` (fast, no dependencies) and the monitoring system at
`/health/ready` every 60 seconds; alert on Unhealthy, ticket on Degraded. Health polls are logged at Debug
so they do not fill the Information stream.

## 6. Correlating a support ticket

1. The user sees an error page or a 5xx; the admin SPA shows the `correlationId` from the ProblemDetails
   body (or the operator reads the `X-Correlation-Id` response header from the browser's network panel).
2. Search the log for `CorrelationId=<id>`: the request line, any warnings and the exception are there.
3. `SELECT * FROM AuditLog WHERE CorrelationId = '<id>'` shows what the request changed before it failed.

A load balancer or IIS ARR that sets `X-Correlation-Id` on the way in gets it back unchanged (token
characters only; anything else is stripped), so the edge's own request id can be the search key.
