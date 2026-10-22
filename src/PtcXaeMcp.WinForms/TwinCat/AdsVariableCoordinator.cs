using System.Text.Json;
using PtcXaeMcp.WinForms;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// ads-variable-rw: validates input and shapes ADS variable read/write results
/// into JSON for the MCP layer. Honest errors are returned as "[Error] ..."
/// strings, never a faked success. Mirrors EventLoggerCoordinator.
/// </summary>
internal sealed class AdsVariableCoordinator(ITwinCatSession session)
{
    private const int DefaultAdsPort = 851;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// A failed ADS call as "[Error] ... (target netId:port)": the NetId the call actually went to, so a wrong default
    /// (issue #202: the registry's stale AmsNetId instead of the AMS router's) shows in the error, not only in a success.
    /// The target is not repeated when the message already names it.
    /// </summary>
    internal static string FailedAt(string message, string? netId, int port)
    {
        string error = McpErrors.EnsureError(message);
        if (string.IsNullOrWhiteSpace(netId) || error.Contains(netId, StringComparison.Ordinal)) return error;
        return $"{error} (target {netId}:{port})";
    }

    public string ReadAdsVariable(string symbolPath, string? amsNetId, int? adsPort)
    {
        if (string.IsNullOrWhiteSpace(symbolPath))
            return "[Error] symbolPath is required.";

        var r = session.ReadAdsVariable(symbolPath.Trim(), amsNetId, adsPort ?? DefaultAdsPort);
        if (!r.Success)
            return FailedAt(r.Message, r.AmsNetId, r.AdsPort);

        return JsonSerializer.Serialize(new
        {
            success = true,
            symbol = r.Symbol,
            plcType = r.PlcType,
            value = r.Value,
            amsNetId = r.AmsNetId,
            adsPort = r.AdsPort,
        }, JsonOpts);
    }

    /// <summary>
    /// ibn-live: read many symbols in one call. Returns
    /// { success, results: [{ symbol, plcType, value, ok, message }], amsNetId, adsPort }
    /// or an "[Error] ..." string on a connection-level failure. Per-symbol
    /// failures appear as items with ok=false — they do not fail the whole call.
    /// </summary>
    public string ReadAdsVariablesBatch(IReadOnlyList<string>? symbolPaths, string? amsNetId, int? adsPort)
    {
        if (symbolPaths is null || symbolPaths.Count == 0)
            return "[Error] symbolPaths is required (at least one symbol).";

        var cleaned = symbolPaths
            .Select(s => (s ?? string.Empty).Trim())
            .Where(s => s.Length > 0)
            .ToList();
        if (cleaned.Count == 0)
            return "[Error] symbolPaths contained only empty entries.";

        var r = session.ReadAdsVariablesBatch(cleaned, amsNetId, adsPort ?? DefaultAdsPort);
        if (!r.Success)
            return FailedAt(r.Message, r.AmsNetId, r.AdsPort);

        return JsonSerializer.Serialize(new
        {
            success = true,
            count = r.Results.Count,
            results = r.Results.Select(i => new
            {
                symbol = i.Symbol,
                plcType = i.PlcType,
                value = i.Value,
                ok = i.Success,
                message = i.Message,
            }),
            amsNetId = r.AmsNetId,
            adsPort = r.AdsPort,
        }, JsonOpts);
    }

    private const int DefaultTransitionTimeoutMs = 5000;
    private const int MaxTransitionTimeoutMs = 60000;

    /// <summary>
    /// ibn-live: measure the elapsed time between two symbol conditions
    /// (edge A → edge B). Returns { success, elapsedMs, timedOut } or "[Error]".
    /// A read-only cross-check; moves nothing.
    /// </summary>
    public string MeasureSignalTransition(string startSymbol, string? startEquals,
        string endSymbol, string? endEquals, int? timeoutMs, string? amsNetId, int? adsPort)
    {
        if (string.IsNullOrWhiteSpace(startSymbol)) return "[Error] startSymbol is required.";
        if (string.IsNullOrWhiteSpace(endSymbol)) return "[Error] endSymbol is required.";

        int timeout = timeoutMs is null or <= 0
            ? DefaultTransitionTimeoutMs
            : Math.Min(timeoutMs.Value, MaxTransitionTimeoutMs);

        var r = session.MeasureSignalTransition(
            startSymbol.Trim(), (startEquals ?? "true").Trim(),
            endSymbol.Trim(), (endEquals ?? "true").Trim(),
            timeout, amsNetId, adsPort ?? DefaultAdsPort);

        if (!r.Success)
            return McpErrors.EnsureError(r.Message);

        return JsonSerializer.Serialize(new
        {
            success = true,
            elapsedMs = r.ElapsedMs,
            timedOut = r.TimedOut,
            message = r.Message,
        }, JsonOpts);
    }

    /// <summary>
    /// READ-ONLY diagnostic: dumps an FB/struct instance's MemberInstances
    /// (including PROPERTY-flagged entries) and RPC-callable methods. Used to
    /// determine empirically whether a given PROPERTY surfaces as an
    /// addressable symbol or only via an RPC getter/setter.
    /// </summary>
    public string InspectFbInstance(string instancePath, string? amsNetId, int? adsPort)
    {
        if (string.IsNullOrWhiteSpace(instancePath))
            return "[Error] instancePath is required.";

        var r = session.InspectFbInstance(instancePath.Trim(), amsNetId, adsPort ?? DefaultAdsPort);
        if (!r.Success)
            return FailedAt(r.Message, r.AmsNetId, r.AdsPort);

        return JsonSerializer.Serialize(new
        {
            success = true,
            instancePath = r.InstancePath,
            typeName = r.TypeName,
            members = r.Members.Select(m => new
            {
                name = m.Name,
                instancePath = m.InstancePath,
                isProperty = m.IsProperty,
                typeName = m.TypeName,
                size = m.Size,
                offset = m.Offset,
            }),
            rpcMethods = r.RpcMethods.Select(m => new
            {
                name = m.Name,
                isVoid = m.IsVoid,
                parameterCount = m.ParameterCount,
            }),
            amsNetId = r.AmsNetId,
            adsPort = r.AdsPort,
        }, JsonOpts);
    }

    public string WriteAdsVariable(string symbolPath, string value, string? amsNetId, int? adsPort)
    {
        if (string.IsNullOrWhiteSpace(symbolPath))
            return "[Error] symbolPath is required.";
        if (value is null)
            return "[Error] value is required.";

        var r = session.WriteAdsVariable(symbolPath.Trim(), value, amsNetId, adsPort ?? DefaultAdsPort);
        if (!r.Success)
            return FailedAt(r.Message, r.AmsNetId, r.AdsPort);

        return JsonSerializer.Serialize(new
        {
            success = true,
            symbol = r.Symbol,
            plcType = r.PlcType,
            writtenValue = r.Value,
            amsNetId = r.AmsNetId,
            adsPort = r.AdsPort,
        }, JsonOpts);
    }
}
