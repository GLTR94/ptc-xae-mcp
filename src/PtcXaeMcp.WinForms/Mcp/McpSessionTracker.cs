using Microsoft.AspNetCore.Http;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using PtcXaeMcp.WinForms.Diagnostics;

namespace PtcXaeMcp.WinForms.Mcp;

/// <summary>
/// Registers every connected MCP client into <see cref="McpSessionRegistry"/>
/// at the real connect boundary — the MCP <c>initialize</c> handshake — via the
/// SDK's <see cref="ISessionMigrationHandler"/> hook.
///
/// <para><b>Why (issue #71).</b> The previous design relied on "every tool call
/// Touches the session", but the only call site wired to do that was the
/// <c>twincat_get_client_info</c> tool, so the host header was stuck at
/// <c>0 clients</c> while clients were actively working. ModelContextProtocol
/// 1.3.0 exposes no connect/disconnect event on <c>McpServer</c>, but the
/// AspNetCore Streamable-HTTP transport calls
/// <see cref="OnSessionInitializedAsync"/> once per session right after the
/// handshake, with the assigned session id and the client's self-reported
/// identity. Registering there counts every client correctly with zero
/// per-tool changes. A request-level refresh keeps long-lived but quiet
/// sessions from being swept mid-work; the existing TTL sweep still evicts
/// genuinely-gone sessions (HTTP clients vanish without a clean close).</para>
///
/// <para><b>Trust boundary:</b> client name/version are self-reported and
/// unauthenticated — diagnostics/observability only; never gate behaviour.</para>
/// </summary>
internal sealed class McpSessionTracker(McpSessionRegistry registry) : ISessionMigrationHandler
{
    /// <summary>Header carrying the session id on every post-initialize request.</summary>
    public const string SessionIdHeader = "Mcp-Session-Id";

    public ValueTask OnSessionInitializedAsync(
        HttpContext httpContext,
        string sessionId,
        InitializeRequestParams initializeRequestParams,
        CancellationToken cancellationToken)
    {
        var info = initializeRequestParams.ClientInfo;
        var clientName = string.IsNullOrWhiteSpace(info?.Name) ? "(unknown)" : info!.Name;
        var clientVersion = string.IsNullOrWhiteSpace(info?.Version) ? "(unknown)" : info!.Version;
        var sid = string.IsNullOrWhiteSpace(sessionId) ? "(unknown)" : sessionId;

        registry.Touch(new McpClientSession(
            SessionId: sid,
            ClientName: clientName,
            ClientVersion: clientVersion,
            NegotiatedProtocolVersion:
                string.IsNullOrWhiteSpace(initializeRequestParams.ProtocolVersion)
                    ? "(unknown)" : initializeRequestParams.ProtocolVersion!,
            ConnectedAt: DateTimeOffset.UtcNow));

        HostLog.Shared.Info($"Session connected: {clientName} {clientVersion} (session {sid}).");
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// This is a single-instance host — there is no peer to migrate a session
    /// from, so an unrecognised <c>Mcp-Session-Id</c> has no persisted
    /// initialization data. Returning <c>null</c> declines the migration and
    /// preserves the SDK's default "unknown session" handling.
    /// </summary>
    public ValueTask<InitializeRequestParams?> AllowSessionMigrationAsync(
        HttpContext httpContext,
        string sessionId,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<InitializeRequestParams?>(null);
}
