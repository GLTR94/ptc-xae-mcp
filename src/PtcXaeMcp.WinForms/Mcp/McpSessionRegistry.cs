namespace PtcXaeMcp.WinForms.Mcp;

/// <summary>
/// One connected MCP client session.
/// <para>
/// <b>Trust boundary:</b> <see cref="ClientName"/> / <see cref="ClientVersion"/> are
/// taken verbatim from the client's <c>initialize</c> handshake. They are
/// self-reported and unauthenticated — a client may claim any value. Use for
/// observability/diagnostics only; never gate behaviour on them.
/// </para>
/// </summary>
public sealed record McpClientSession(
    string SessionId,
    string ClientName,
    string ClientVersion,
    string NegotiatedProtocolVersion,
    DateTimeOffset ConnectedAt)
{
    /// <summary>Last time this session was observed (refreshed on every tool call).</summary>
    public DateTimeOffset LastSeen { get; init; } = ConnectedAt;
}

/// <summary>
/// Thread-safe registry of currently-connected MCP client sessions.
/// <para>
/// ModelContextProtocol 1.3.0 exposes no connect/disconnect event, so the registry
/// is populated lazily: every tool call <see cref="Touch"/>es the calling session
/// (keyed by the SDK's <c>SessionId</c>), and a periodic <see cref="Sweep"/> evicts
/// sessions whose <see cref="McpClientSession.LastSeen"/> is older than a TTL —
/// HTTP/SSE clients frequently vanish without a clean close.
/// </para>
/// </summary>
public sealed class McpSessionRegistry
{
    /// <summary>
    /// Client name of the GUI's own tool console (#271). Its in-process session is not an
    /// external client, so it is never registered (header count, /health connectedSessions).
    /// </summary>
    internal const string ConsoleClientName = "ptc-xae-mcp console";

    private readonly object _gate = new();
    private readonly Dictionary<string, McpClientSession> _sessions = new(StringComparer.Ordinal);

    /// <summary>Raised after any mutation (add/update/remove/sweep). May fire on any thread.</summary>
    public event Action? Changed;

    public int Count
    {
        get { lock (_gate) return _sessions.Count; }
    }

    /// <summary>
    /// Records or refreshes a session. First sighting inserts it; a repeat sighting
    /// for the same <see cref="McpClientSession.SessionId"/> updates the entry in
    /// place and bumps <see cref="McpClientSession.LastSeen"/>.
    /// </summary>
    public void Touch(McpClientSession session, DateTimeOffset? seenAt = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.Equals(session.ClientName, ConsoleClientName, StringComparison.Ordinal))
            return;
        var now = seenAt ?? DateTimeOffset.UtcNow;

        lock (_gate)
        {
            if (_sessions.TryGetValue(session.SessionId, out var existing))
            {
                // Keep the original ConnectedAt; refresh identity + last-seen.
                _sessions[session.SessionId] = session with
                {
                    ConnectedAt = existing.ConnectedAt,
                    LastSeen = now,
                };
            }
            else
            {
                _sessions[session.SessionId] = session with { LastSeen = now };
            }
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// Bumps <see cref="McpClientSession.LastSeen"/> for an already-registered
    /// session without changing identity or count. Used by the per-request
    /// keep-alive so a long-lived but quiet client (e.g. mid-build) is not
    /// swept by the TTL. No-op for an unknown id; never raises
    /// <see cref="Changed"/> (the visible client set is unchanged).
    /// </summary>
    public void Refresh(string sessionId, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(sessionId))
            return;

        lock (_gate)
        {
            if (_sessions.TryGetValue(sessionId, out var existing))
                _sessions[sessionId] = existing with { LastSeen = now };
        }
    }

    /// <summary>Removes a session by id. Returns true if it was present.</summary>
    public bool Remove(string sessionId)
    {
        bool removed;
        lock (_gate)
            removed = _sessions.Remove(sessionId);

        if (removed)
            Changed?.Invoke();
        return removed;
    }

    /// <summary>
    /// Evicts sessions not seen within <paramref name="ttl"/>. Returns the count removed.
    /// </summary>
    public int Sweep(TimeSpan ttl, DateTimeOffset now)
    {
        List<string>? stale = null;
        lock (_gate)
        {
            foreach (var (id, s) in _sessions)
                if (now - s.LastSeen > ttl)
                    (stale ??= []).Add(id);

            if (stale is not null)
                foreach (var id in stale)
                    _sessions.Remove(id);
        }

        if (stale is { Count: > 0 })
        {
            Changed?.Invoke();
            return stale.Count;
        }
        return 0;
    }

    /// <summary>Point-in-time copy of all sessions, newest connection first.</summary>
    public IReadOnlyList<McpClientSession> Snapshot()
    {
        lock (_gate)
            return _sessions.Values
                .OrderByDescending(s => s.ConnectedAt)
                .ToArray();
    }

    /// <summary>
    /// The most-recently-connected session, or null if none. Used as the
    /// degraded fallback for <c>twincat_get_client_info</c> when a per-call
    /// session cannot be resolved.
    /// </summary>
    public McpClientSession? MostRecent()
    {
        lock (_gate)
        {
            McpClientSession? best = null;
            foreach (var s in _sessions.Values)
                if (best is null || s.ConnectedAt > best.ConnectedAt)
                    best = s;
            return best;
        }
    }
}
