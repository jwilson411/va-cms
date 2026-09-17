using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace VA.CMS.Infrastructure.Data.Migrations;

/// <summary>
/// Minimal SQLCMD-style runner for the infra/sql scripts (#157): substitutes
/// <c>$(Name)</c> variables and executes each <c>GO</c>-separated batch. Lets the
/// CLI and the integration-test fixture provision logins without sqlcmd installed.
/// </summary>
public static class SqlCmdScript
{
    private static readonly Regex GoSeparator = new(@"^\s*GO\s*(?:--.*)?$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
    private static readonly Regex Variable    = new(@"\$\((?<name>[A-Za-z_][A-Za-z0-9_]*)\)");

    /// <summary>Replaces every <c>$(Name)</c>; throws when a variable has no value (never sends a literal token to SQL).</summary>
    public static string Substitute(string script, IReadOnlyDictionary<string, string> variables)
        => Variable.Replace(script, m =>
        {
            var name = m.Groups["name"].Value;
            return variables.TryGetValue(name, out var value)
                ? value
                : throw new ArgumentException($"SQLCMD variable $({name}) was not supplied.");
        });

    /// <summary>GO-separated batches, minus empty and comment-only ones.</summary>
    public static IReadOnlyList<string> SplitBatches(string script)
        => GoSeparator.Split(script)
            .Select(b => b.Trim())
            .Where(b => b.Length > 0 && !IsCommentOnly(b))
            .ToList();

    public static async Task RunAsync(
        string connectionString,
        string script,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken ct = default)
    {
        var substituted = Substitute(script, variables);

        await using var conn = new SqlConnection(connectionString);
        await conn.OpenAsync(ct);
        foreach (var batch in SplitBatches(substituted))
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = batch;
            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    private static bool IsCommentOnly(string batch)
        => batch.Split('\n').All(l => l.Trim().Length == 0 || l.TrimStart().StartsWith("--", StringComparison.Ordinal));
}
