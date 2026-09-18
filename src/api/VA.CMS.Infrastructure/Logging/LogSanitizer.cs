namespace VA.CMS.Infrastructure.Logging;

/// <summary>
/// Log-forging guard (#166). The JSON sinks escape control characters already, but a value
/// that came off the wire (request path, header, claim) is scrubbed before it is used as a
/// log property so that no sink configuration — plain-text console in Development, a
/// third-party formatter — can be tricked into emitting a fabricated line.
/// </summary>
public static class LogSanitizer
{
    public const int MaxLength = 512;

    /// <summary>
    /// Replaces CR/LF and every other control character with '_' and truncates to
    /// <see cref="MaxLength"/> characters. Null → "".
    /// </summary>
    public static string Scrub(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        var truncated = value.Length > MaxLength;
        var span = truncated ? value.AsSpan(0, MaxLength) : value.AsSpan();

        Span<char> buffer = stackalloc char[MaxLength + 1];
        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            buffer[i] = char.IsControl(c) ? '_' : c;
        }
        if (truncated) buffer[span.Length] = '…';

        return new string(buffer[..(span.Length + (truncated ? 1 : 0))]);
    }
}
