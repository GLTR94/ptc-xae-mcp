using System.Security.Cryptography;
using System.Text;

namespace PtcXaeMcp.WinForms.Security;

/// <summary>
/// Pure decision logic for the localhost-only hardening of the MCP HTTP
/// server (#156 item 7): reject a request whose <c>Host</c> header is not
/// one of the three loopback forms on the server's port, reject a request
/// whose <c>Origin</c> header (if present — most non-browser MCP clients
/// never send one) is not an http(s) localhost origin (DNS rebinding from
/// a browser), and — only when <c>PTC_XAE_MCP_TOKEN</c> is set — require a
/// matching <c>Authorization: Bearer</c> header. Kept free of
/// <c>HttpContext</c> so it is unit-testable; <see cref="McpBridge"/> wires
/// it into the ASP.NET pipeline.
/// </summary>
internal static class LocalhostGuard
{
    /// <summary>Env var carrying the optional bearer token. Auth is not
    /// required when this is unset or empty.</summary>
    public const string TokenEnvVar = "PTC_XAE_MCP_TOKEN";

    private static readonly string[] LoopbackHosts = ["localhost", "127.0.0.1", "[::1]"];

    public static bool IsAllowedHost(string? hostHeader, int port)
    {
        if (string.IsNullOrEmpty(hostHeader)) return false;
        foreach (string loopback in LoopbackHosts)
        {
            if (string.Equals(hostHeader, $"{loopback}:{port}", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>No Origin header at all is allowed — that's the normal case
    /// for a non-browser MCP client (stdio-bridged Claude Code, curl, …).
    /// A present Origin header must be an http(s) loopback origin.</summary>
    public static bool IsAllowedOrigin(string? originHeader)
    {
        if (string.IsNullOrEmpty(originHeader)) return true;

        if (!Uri.TryCreate(originHeader, UriKind.Absolute, out Uri? uri)) return false;
        if (uri.Scheme is not ("http" or "https")) return false;

        return uri.Host is "localhost" or "127.0.0.1" or "::1" or "[::1]";
    }

    /// <summary>True when no token is configured (auth not required) or the
    /// presented "Bearer &lt;token&gt;" header matches, compared in constant
    /// time to avoid a timing side channel.</summary>
    public static bool IsAuthorized(string? authorizationHeader, string? configuredToken)
    {
        if (string.IsNullOrEmpty(configuredToken)) return true;

        const string prefix = "Bearer ";
        if (string.IsNullOrEmpty(authorizationHeader) ||
            !authorizationHeader.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        string presented = authorizationHeader[prefix.Length..];
        byte[] presentedBytes = Encoding.UTF8.GetBytes(presented);
        byte[] configuredBytes = Encoding.UTF8.GetBytes(configuredToken);
        if (presentedBytes.Length != configuredBytes.Length) return false;
        return CryptographicOperations.FixedTimeEquals(presentedBytes, configuredBytes);
    }

    /// <summary>Whether the server currently requires the bearer token —
    /// what <c>/health</c> reports as <c>authRequired</c>.</summary>
    public static bool AuthRequired() =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(TokenEnvVar));
}
