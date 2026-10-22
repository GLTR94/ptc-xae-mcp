namespace PtcXaeMcp.McpTests;

// Serializes McpServerTests and IbnLiveMcpTests so they never run in parallel.
// Both share a single MCP server (port 5010) with a single-operation lock; parallel
// execution causes "[Error] An operation is already in progress" on long-running
// calls like T91_OpenLicenseManagerAndRequestTrial (20 s).
[CollectionDefinition("McpServer")]
public class McpServerCollection { }
