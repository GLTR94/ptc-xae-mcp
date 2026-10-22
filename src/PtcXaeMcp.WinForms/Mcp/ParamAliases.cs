namespace PtcXaeMcp.WinForms;

/// <summary>
/// Shared coalescing helper for tool parameters renamed for issue #157 item 1
/// (e.g. RuntimeControl's <c>netId</c> → <c>amsNetId</c>, IoMapping's
/// <c>v1</c>/<c>v2</c>/<c>offs1</c>/<c>offs2</c> → descriptive names).
/// <para>
/// MCP clients bind tool arguments by parameter name, so a rename breaks any
/// caller still sending the old name. For one release, each renamed parameter
/// keeps its old name as an extra, optional, documented-deprecated parameter;
/// the tool method resolves the effective value with <see cref="Coalesce"/> —
/// preferring the new name when both are supplied (a client migrating
/// mid-transition wins with the name it is actively updating to), falling
/// back to the deprecated name otherwise.
/// </para>
/// </summary>
internal static class ParamAliases
{
    /// <summary>
    /// Returns <paramref name="preferred"/> when it is non-null/non-empty,
    /// otherwise <paramref name="deprecated"/> (which may itself be null/empty).
    /// </summary>
    internal static string? Coalesce(string? preferred, string? deprecated)
        => !string.IsNullOrEmpty(preferred) ? preferred : deprecated;

    /// <summary>
    /// Returns <paramref name="preferred"/> when <paramref name="deprecated"/>
    /// was not supplied (null), otherwise <paramref name="deprecated"/> — for
    /// value-typed parameters (e.g. bit offsets) where the "preferred" side
    /// carries a non-null default (0) rather than being itself optional, so the
    /// deprecated alias must be the nullable one that signals "was it supplied".
    /// </summary>
    internal static int Coalesce(int preferred, int? deprecated)
        => deprecated ?? preferred;
}
