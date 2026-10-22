namespace PtcXaeMcp.WinForms.Mcp;

/// <summary>
/// One traced MCP tool call: when it ran, which tool, the (possibly
/// truncated) argument and response text, how long it took, and whether
/// it succeeded.
/// <para>
/// <b>Trust / privacy boundary:</b> <see cref="Arguments"/> and
/// <see cref="Response"/> are captured verbatim from tool calls and may
/// contain absolute paths or source. This is a local developer tool;
/// the activity log is observability-only and is never sent anywhere.
/// </para>
/// </summary>
public sealed record McpActivityEntry(
    DateTimeOffset TimestampUtc,
    string ToolName,
    string Arguments,
    string Response,
    TimeSpan Duration,
    bool Success)
{
    /// <summary>
    /// Max characters kept for <see cref="Arguments"/> / <see cref="Response"/>.
    /// Tool payloads (whole POU bodies, schema JSON) can be huge; the buffer
    /// and the JSON log would explode otherwise. ~8 KB per field.
    /// </summary>
    public const int MaxFieldChars = 8 * 1024;

    public static McpActivityEntry Ok(
        string toolName, string? arguments, string? response,
        DateTimeOffset startedUtc, TimeSpan duration) =>
        new(startedUtc, toolName, Truncate(arguments), Truncate(response),
            duration, Success: true);

    public static McpActivityEntry Failure(
        string toolName, string? arguments, string? error,
        DateTimeOffset startedUtc, TimeSpan duration) =>
        new(startedUtc, toolName, Truncate(arguments), Truncate(error),
            duration, Success: false);

    /// <summary>
    /// Clamps oversized text to <see cref="MaxFieldChars"/> and appends a
    /// marker noting how much was dropped. Null becomes empty.
    /// </summary>
    public static string Truncate(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        if (text.Length <= MaxFieldChars)
            return text;

        int dropped = text.Length - MaxFieldChars;
        return text[..MaxFieldChars] + $" …(truncated, {dropped} characters)";
    }
}
