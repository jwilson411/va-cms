using System.Data;
using PetaPoco;

namespace VA.CMS.Infrastructure.Data;

/// <summary>
/// PetaPoco database context for VA CMS. Registered as AddScoped&lt;CmsDatabase&gt; in DI.
/// All queries go through stored procedures — no raw DML from this layer.
///
/// #165: when the scope has an actor, every connection this instance opens gets the
/// actor id, source IP, user agent and correlation id written to SESSION_CONTEXT
/// before the first command. usp_AuditLog_Write reads those keys whenever a caller
/// leaves the matching parameter NULL, so a stored procedure that audits inside
/// its own transaction records who/where without a signature change. Pooled
/// connections are reset by SQL Server on return, which clears the context, so
/// it must be set on every open.
/// </summary>
public class CmsDatabase : Database
{
    private readonly IAuditContext _audit;

    public CmsDatabase(string connectionString, IAuditContext? audit = null)
        : base(connectionString, Microsoft.Data.SqlClient.SqlClientFactory.Instance)
    {
        _audit = audit ?? AuditContext.Empty;
    }

    /// <summary>The audit context this instance stamps onto its connections.</summary>
    public IAuditContext Audit => _audit;

    public override IDbConnection OnConnectionOpened(IDbConnection conn)
    {
        // Anonymous requests (public site reads, health checks) and background
        // workers have no actor: skip the round trip. Their audit rows, if any,
        // are written from C# with explicit parameters.
        if (_audit.ActorId is { } actorId)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "EXEC sp_set_session_context @key = N'ActorId',       @value = @actor;" +
                "EXEC sp_set_session_context @key = N'SourceIp',      @value = @ip;" +
                "EXEC sp_set_session_context @key = N'UserAgent',     @value = @ua;" +
                "EXEC sp_set_session_context @key = N'CorrelationId', @value = @cid;";
            AddParam(cmd, "@actor", actorId);
            AddParam(cmd, "@ip",    _audit.SourceIp);
            AddParam(cmd, "@ua",    _audit.UserAgent);
            AddParam(cmd, "@cid",   _audit.CorrelationId);
            cmd.ExecuteNonQuery();
        }

        return base.OnConnectionOpened(conn);
    }

    private static void AddParam(IDbCommand cmd, string name, object? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value ?? DBNull.Value;
        cmd.Parameters.Add(p);
    }
}
