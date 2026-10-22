using System.Text.Json;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// hmi-internal-symbols (issue #165): HMI-owned variables
/// without a PLC behind them. Two mechanisms, deliberately kept apart in every tool:
///  A. <b>Client (framework) internal symbols</b> <c>%i%Name%/i%</c> — ITcHmiProject.AddInternalSymbol
///     / RemoveInternalSymbol, persisted in <c>Properties\tchmiconfig.json</c>.
///  B. <b>Server symbols</b> in the TcHmiSrv domain — ITcHmiServer.MapOfflineSymbol, persisted by
///     the server in <c>Server\TcHmiSrv\TcHmiSrv.Config.default.json</c>.
/// Decompiled TE2000 14.17.22.1, not yet verified live: AddInternalSymbol is void and has no
/// duplicate check, RemoveInternalSymbol returns false even on success, MapOfflineSymbol's bool is
/// mostly honest. So every write is decided by a read-back of the persisted state only.
/// </summary>
internal sealed partial class HmiCoordinator
{
    /// <summary>Waits between read-back attempts. SaveResync may write tchmiconfig.json
    /// asynchronously, so a missing entry is re-read a few times before the write counts as
    /// failed. A seam so tests do not sleep.</summary>
    internal IReadOnlyList<TimeSpan> ReadBackDelays { get; init; } =
    [
        TimeSpan.Zero, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2),
    ];

    // ---- read-only -----------------------------------------------------------

    public (string Summary, string Json) ListInternalSymbols(string? hmiProject)
    {
        string? error = SessionGuards.RequireOpenSolution(Session);
        if (error is not null) return (error, "{}");

        // Files only — no server call, so a project that is still loading can be listed too.
        var (project, resolveError) = ResolveProject(hmiProject, requireReady: false);
        if (project is null) return (resolveError!, "{}");

        var (client, clientPath, clientError) = HmiInternalSymbols.ReadClientSymbols(project.ProjectDirectory);
        if (client is null) return ($"[Error] {clientError} Nothing was read.", "{}");

        // The server table is the second mechanism; an unreadable file is reported as unknown,
        // never as "no server symbols" (same rule as the ADS runtimes in ListTmcFiles).
        var (server, serverPath, serverError) = HmiInternalSymbols.ReadServerSymbols(project.ProjectDirectory);
        List<HmiServerSymbolEntry>? user = server?.Where(HmiInternalSymbols.IsUserServerSymbol).ToList();

        string summary = client.Count == 0
            ? $"HMI project '{project.Name}' has no client internal symbol (%i%) in {HmiInternalSymbols.ClientConfigRelativePath}."
            : $"{client.Count} client internal symbol(s) (%i%) in '{project.Name}': " +
              string.Join(", ", client.Take(20).Select(HmiInternalSymbols.Describe)) +
              (client.Count > 20 ? $" … +{client.Count - 20} more." : ".");
        summary += user is null
            ? $" Server symbols of the {HmiInternalSymbols.ServerDomain} domain are UNKNOWN ({serverError})."
            : user.Count == 0
                ? $" No user server symbol in the {HmiInternalSymbols.ServerDomain} domain."
                : $" {user.Count} user server symbol(s) in the {HmiInternalSymbols.ServerDomain} domain " +
                  $"({user.Count(s => !s.UseMapping)} offline): " +
                  string.Join(", ", user.Take(20).Select(HmiInternalSymbols.Describe)) +
                  (user.Count > 20 ? $" … +{user.Count - 20} more." : ".");

        string json = JsonSerializer.Serialize(new
        {
            hmiProject = project.Name,
            client = new { file = clientPath, count = client.Count, symbols = client },
            server = new
            {
                file = serverPath,
                readable = user is not null,
                error = user is null ? serverError : null,
                domain = HmiInternalSymbols.ServerDomain,
                count = user?.Count,
                offline = user?.Count(s => !s.UseMapping),
                symbols = user,
            },
        }, JsonOpts);
        return (summary, json);
    }

    // ---- A. client internal symbols (confirmed by the engineer) ---------------

    /// <summary>
    /// Add a client internal symbol. validate → resolve → read tchmiconfig.json → refuse an
    /// existing name (AddInternalSymbol would store it twice) → popup → AddInternalSymbol →
    /// read-back of tchmiconfig.json (polled: the save may be asynchronous).
    /// </summary>
    public (string Summary, string Json) AddInternalSymbol(
        string? hmiProject, string? name, string? dataType, string? defaultValueJson,
        bool persist, bool readOnly, int confirmTimeoutSeconds)
    {
        string n = name?.Trim() ?? "";
        if (!HmiInternalSymbols.IsValidClientName(n))
            return ($"[Error] name '{n}' is not a valid internal symbol name — letters, digits and '_' only, " +
                    $"starting with a letter or '_', max {HmiInternalSymbols.MaxNameLength} characters " +
                    "(it is referenced as %i%name%/i%).", "{}");
        string type = dataType?.Trim() ?? "";
        if (!HmiInternalSymbols.IsValidDataType(type))
            return ($"[Error] {HmiInternalSymbols.DataTypeHint}", "{}");
        if (!HmiInternalSymbols.TryNormalizeDefault(defaultValueJson, type, out string? value, out string? valueError))
            return ($"[Error] {valueError}", "{}");
        if (TimeoutError(confirmTimeoutSeconds) is { } timeoutError) return (timeoutError, "{}");

        string? error = SessionGuards.RequireOpenSolution(Session);
        if (error is not null) return (error, "{}");
        var (project, resolveError) = ResolveProject(hmiProject);
        if (project is null) return (resolveError!, "{}");

        var (before, file, readError) = HmiInternalSymbols.ReadClientSymbols(project.ProjectDirectory);
        if (before is null) return ($"[Error] {readError} Nothing was written.", "{}");
        HmiClientInternalSymbol? existing = before.FirstOrDefault(s => s.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return ($"[Error] '{project.Name}' already has the internal symbol '{existing.Name}' " +
                    $"({HmiInternalSymbols.Describe(existing)}) — nothing was written. TE2000 adds without a " +
                    "duplicate check, so it is never added twice; remove it first " +
                    "(twincat_hmi_remove_internal_symbol) to change it.", "{}");

        var request = new HmiWriteRequest(HmiWriteKind.AddInternalSymbol, project.Name, project.ProjectDirectory,
            n, "client (%i%)",
            NewValue: $"{type} = {value ?? "(no default)"}" + (persist ? ", persist" : "") + (readOnly ? ", read-only" : ""),
            CurrentValue: $"(does not exist; {before.Count} internal symbol(s))", WritesToPlc: false);
        var (outcome, refusal) = AskEngineer(request, confirmTimeoutSeconds);
        if (refusal is not null) return (refusal, "{}");

        HmiOperationResult result;
        try { result = Session.AddHmiInternalSymbol(project.Name, n, type, value, persist, readOnly); }
        catch (Exception ex) when (IsHmiFailure(ex)) { return (WithDialogHint($"[Error] AddInternalSymbol failed: {ex.Message}"), "{}"); }

        var (after, afterError) = PollClientSymbols(project.ProjectDirectory,
            list => list.Any(s => s.Name.Equals(n, StringComparison.Ordinal)));
        if (after is null)
            return ($"[Error] AddInternalSymbol was issued, but the read-back failed ({afterError}) — whether it " +
                    "took effect is unknown. Check with twincat_hmi_list_internal_symbols.", "{}");
        HmiClientInternalSymbol? landed = after.FirstOrDefault(s => s.Name.Equals(n, StringComparison.Ordinal));
        if (landed is null)
            return ($"[Error] AddInternalSymbol reported no exception, but '{n}' is not in {file} on read-back " +
                    "— not treated as success." +
                    (result.LastError is null ? "" : $" Last error: {result.LastError}."), "{}");

        var mismatches = new List<string>();
        if (!string.Equals(landed.Type, type, StringComparison.Ordinal)) mismatches.Add($"type is '{landed.Type}'");
        if (!HmiInternalSymbols.SameJson(landed.ValueJson, value)) mismatches.Add($"value is {landed.ValueJson ?? "(none)"}");
        if (landed.Persist != persist) mismatches.Add($"persist is {landed.Persist}");
        if (landed.ReadOnly != readOnly) mismatches.Add($"readonly is {landed.ReadOnly}");

        string summary = $"Client internal symbol %i%{landed.Name}%/i% added to '{project.Name}' — {Describe(outcome)}, " +
                         $"verified in {HmiInternalSymbols.ClientConfigRelativePath} " +
                         $"({HmiInternalSymbols.Describe(landed)})." +
                         (mismatches.Count == 0 ? "" : $" WARNING: stored differently than requested: {string.Join("; ", mismatches)}.");
        string json = JsonSerializer.Serialize(new
        {
            hmiProject = project.Name,
            name = landed.Name,
            reference = $"%i%{landed.Name}%/i%",
            stored = landed,
            mismatches,
            file,
            total = after.Count,
            confirmation = ConfirmationWire(outcome),
        }, JsonOpts);
        return (summary, json);
    }

    /// <summary>
    /// Remove a client internal symbol. Not there → "Nothing to do" without a popup (hmi-server
    /// E8). RemoveInternalSymbol's bool is always false, so only its absence on read-back counts.
    /// </summary>
    public (string Summary, string Json) RemoveInternalSymbol(string? hmiProject, string? name, int confirmTimeoutSeconds)
    {
        string n = name?.Trim() ?? "";
        if (n.Length == 0 || n.Length > HmiInternalSymbols.MaxMapNameLength)
            return ("[Error] name is required — the internal symbol to remove (twincat_hmi_list_internal_symbols " +
                    "shows them).", "{}");
        if (TimeoutError(confirmTimeoutSeconds) is { } timeoutError) return (timeoutError, "{}");

        string? error = SessionGuards.RequireOpenSolution(Session);
        if (error is not null) return (error, "{}");
        var (project, resolveError) = ResolveProject(hmiProject);
        if (project is null) return (resolveError!, "{}");

        var (before, file, readError) = HmiInternalSymbols.ReadClientSymbols(project.ProjectDirectory);
        if (before is null) return ($"[Error] {readError} Nothing was written.", "{}");
        HmiClientInternalSymbol? current = before.FirstOrDefault(s => s.Name.Equals(n, StringComparison.Ordinal));
        if (current is null)
        {
            HmiClientInternalSymbol? otherCase = before.FirstOrDefault(s => s.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
            return ($"Nothing to do: '{project.Name}' has no internal symbol '{n}'" +
                    (otherCase is null ? "." : $" (did you mean '{otherCase.Name}'? Names are case-sensitive)."),
                JsonSerializer.Serialize(new { hmiProject = project.Name, name = n, removed = false }, JsonOpts));
        }

        var request = new HmiWriteRequest(HmiWriteKind.RemoveInternalSymbol, project.Name, project.ProjectDirectory,
            current.Name, "client (%i%)", NewValue: "(removed — bindings to %i%" + current.Name + "%/i% break)",
            CurrentValue: HmiInternalSymbols.Describe(current), WritesToPlc: false);
        var (outcome, refusal) = AskEngineer(request, confirmTimeoutSeconds);
        if (refusal is not null) return (refusal, "{}");

        try { Session.RemoveHmiInternalSymbol(project.Name, current.Name); }
        catch (Exception ex) when (IsHmiFailure(ex)) { return (WithDialogHint($"[Error] RemoveInternalSymbol failed: {ex.Message}"), "{}"); }

        var (after, afterError) = PollClientSymbols(project.ProjectDirectory,
            list => !list.Any(s => s.Name.Equals(current.Name, StringComparison.Ordinal)));
        if (after is null)
            return ($"[Error] RemoveInternalSymbol was issued, but the read-back failed ({afterError}) — whether it " +
                    "took effect is unknown. Check with twincat_hmi_list_internal_symbols.", "{}");
        if (after.Any(s => s.Name.Equals(current.Name, StringComparison.Ordinal)))
            return ($"[Error] RemoveInternalSymbol was issued, but '{current.Name}' is still in {file} on read-back " +
                    "— not treated as success.", "{}");

        string summary = $"Client internal symbol %i%{current.Name}%/i% removed from '{project.Name}' — " +
                         $"{Describe(outcome)}, verified in {HmiInternalSymbols.ClientConfigRelativePath} " +
                         $"({after.Count} left). Bindings that still reference it no longer resolve.";
        string json = JsonSerializer.Serialize(new
        {
            hmiProject = project.Name,
            name = current.Name,
            removed = true,
            previous = current,
            file,
            total = after.Count,
            confirmation = ConfirmationWire(outcome),
        }, JsonOpts);
        return (summary, json);
    }

    // ---- B. server symbols (confirmed by the engineer) ------------------------

    /// <summary>
    /// Create a server symbol in the TcHmiSrv domain. validate → resolve → refuse a name the
    /// server config or the live server already knows → popup → MapOfflineSymbol → read-back of
    /// the symbol's config (live <c>TcHmiSrv.Config::SYMBOLS::&lt;name&gt;</c>, else the saved
    /// TcHmiSrv.Config.default.json): DOMAIN must be TcHmiSrv and USEMAPPING must equal online.
    /// </summary>
    public (string Summary, string Json) MapOfflineSymbol(
        string? hmiProject, string? mapName, string? dataType, string? defaultValueJson,
        bool online, bool persist, int confirmTimeoutSeconds)
    {
        string map = mapName?.Trim() ?? "";
        if (!HmiInternalSymbols.IsValidServerName(map))
            return ($"[Error] mapName '{map}' is not a valid server symbol name — dotted identifiers " +
                    "(letters, digits, '_'), e.g. 'Recipe.SelectedIndex', max " +
                    $"{HmiInternalSymbols.MaxMapNameLength} characters.", "{}");
        string type = dataType?.Trim() ?? "";
        if (!HmiInternalSymbols.IsValidDataType(type))
            return ($"[Error] {HmiInternalSymbols.DataTypeHint}", "{}");
        if (!HmiInternalSymbols.TryNormalizeDefault(defaultValueJson, type, out string? value, out string? valueError))
            return ($"[Error] {valueError}", "{}");
        if (TimeoutError(confirmTimeoutSeconds) is { } timeoutError) return (timeoutError, "{}");

        string? error = SessionGuards.RequireOpenSolution(Session);
        if (error is not null) return (error, "{}");
        var (project, resolveError) = ResolveProject(hmiProject);
        if (project is null) return (resolveError!, "{}");

        // The saved table is a hint (it may be missing before the server's first start); the live
        // probe below is the gate. A name it lists is refused either way.
        var (saved, savedPath, _) = HmiInternalSymbols.ReadServerSymbols(project.ProjectDirectory);
        HmiServerSymbolEntry? listed = saved?.FirstOrDefault(s => s.Name.Equals(map, StringComparison.OrdinalIgnoreCase));
        if (listed is not null)
            return ($"[Error] '{listed.Name}' already exists in the server configuration of '{project.Name}' " +
                    $"(domain {listed.Domain}, {savedPath}) — nothing was written. It is never re-created silently; " +
                    "remove it first (twincat_hmi_unmap_symbol with its domain).", "{}");

        MappingProbe before;
        try { before = ProbeMapping(project.Name, map); }
        catch (Exception ex) when (IsHmiFailure(ex)) { return (WithDialogHint($"[Error] {ex.Message}"), "{}"); }
        if (before.State == MappingState.Mapped)
            return ($"[Error] '{map}' already exists in the running HMI server of '{project.Name}' ({before.Evidence}) " +
                    "— nothing was written.", "{}");
        if (before.State == MappingState.Unknown)
            return ($"[Error] Cannot determine whether '{map}' exists ({before.Evidence}) — nothing was written. " +
                    "Is the HMI engineering server running (twincat_hmi_server_control start)?", "{}");

        var request = new HmiWriteRequest(HmiWriteKind.MapOfflineSymbol, project.Name, project.ProjectDirectory,
            map, HmiInternalSymbols.ServerDomain,
            NewValue: $"{type} = {value ?? "(no default)"}, {(online ? "online (USEMAPPING)" : "offline")}" +
                      (persist ? ", persist" : ""),
            CurrentValue: "(does not exist)", WritesToPlc: false);
        var (outcome, refusal) = AskEngineer(request, confirmTimeoutSeconds);
        if (refusal is not null) return (refusal, "{}");

        HmiOperationResult result;
        try { result = Session.MapHmiOfflineSymbol(project.Name, map, type, value, online, persist); }
        catch (Exception ex) when (IsHmiFailure(ex)) { return (WithDialogHint($"[Error] MapOfflineSymbol failed: {ex.Message}"), "{}"); }
        if (!result.Ok)
            return ($"[Error] MapOfflineSymbol('{map}', {type}) was refused by the HMI server: " +
                    $"{result.LastError ?? "no error text"}. The server's reason is usually in the XAE Error List " +
                    "(twincat_get_error_list).", "{}");

        var (landed, source, readBackError) = ReadBackServerSymbol(project, map);
        if (landed is null)
            return ($"[Error] MapOfflineSymbol returned true, but '{map}' was found neither in the live server " +
                    $"configuration nor in {HmiInternalSymbols.ServerConfigRelativePath} ({readBackError}) — not " +
                    "treated as success.", "{}");
        if (!landed.Domain.Equals(HmiInternalSymbols.ServerDomain, StringComparison.OrdinalIgnoreCase))
            return ($"[Error] MapOfflineSymbol returned true, but '{map}' reads back with domain '{landed.Domain}' " +
                    $"({source}), not {HmiInternalSymbols.ServerDomain} — not treated as success.", "{}");
        if (landed.UseMapping != online)
            return ($"[Error] MapOfflineSymbol returned true, but '{map}' reads back with USEMAPPING={landed.UseMapping} " +
                    $"({source}), requested online={online} — not treated as success.", "{}");

        var mismatches = new List<string>();
        if (landed.DataType is not null && !string.Equals(landed.DataType, type, StringComparison.Ordinal))
            mismatches.Add($"schema type is '{landed.DataType}'");
        if (!HmiInternalSymbols.SameJson(landed.DefaultJson, value))
            mismatches.Add($"default is {landed.DefaultJson ?? "(none)"}");
        if (landed.Persist != persist) mismatches.Add($"persist is {landed.Persist}");

        string summary = $"Server symbol '{map}' created in '{project.Name}' (domain {HmiInternalSymbols.ServerDomain}, " +
                         $"{(online ? "online" : "offline")}) — {Describe(outcome)}, verified from {source} " +
                         $"({HmiInternalSymbols.Describe(landed)})." +
                         (mismatches.Count == 0 ? "" : $" WARNING: stored differently than requested: {string.Join("; ", mismatches)}.");
        string json = JsonSerializer.Serialize(new
        {
            hmiProject = project.Name,
            mapName = map,
            domain = HmiInternalSymbols.ServerDomain,
            online,
            persist,
            stored = landed,
            readBackSource = source,
            mismatches,
            confirmation = ConfirmationWire(outcome),
        }, JsonOpts);
        return (summary, json);
    }

    // ---- read-back -----------------------------------------------------------

    /// <summary>Re-reads tchmiconfig.json until <paramref name="done"/> holds or the delays run
    /// out; returns the last readable state. null = never readable.</summary>
    private (IReadOnlyList<HmiClientInternalSymbol>? Symbols, string? Error) PollClientSymbols(
        string projectDirectory, Func<IReadOnlyList<HmiClientInternalSymbol>, bool> done)
    {
        IReadOnlyList<HmiClientInternalSymbol>? last = null;
        string? lastError = null;
        foreach (TimeSpan delay in ReadBackDelays.DefaultIfEmpty(TimeSpan.Zero))
        {
            if (delay > TimeSpan.Zero) Thread.Sleep(delay);
            var (symbols, _, error) = HmiInternalSymbols.ReadClientSymbols(projectDirectory);
            if (symbols is null) { lastError = error; continue; }
            last = symbols;
            if (done(symbols)) return (symbols, null);
        }
        return (last, last is null ? lastError : null);
    }

    /// <summary>The symbol's config from the live server (<c>TcHmiSrv.Config::SYMBOLS::name</c>),
    /// else from the saved TcHmiSrv.Config.default.json (polled — the server writes it).</summary>
    private (HmiServerSymbolEntry? Entry, string Source, string? Error) ReadBackServerSymbol(
        HmiProjectEntry project, string mapName)
    {
        string configSymbol = HmiInternalSymbols.ServerConfigSymbol(mapName);
        string? liveError;
        try
        {
            IReadOnlyList<HmiSymbolValue> reads = Session.ReadHmiSymbols(project.Name, [configSymbol]);
            HmiSymbolValue? read = reads.Count > 0 ? reads[0] : null;
            HmiServerSymbolEntry? entry = read is { Ok: true } ? HmiInternalSymbols.ParseServerSymbolEntry(mapName, read.Value) : null;
            if (entry is not null) return (entry, $"the live server config '{configSymbol}'", null);
            liveError = read is null ? "no answer" : read.Ok ? "not a symbol object" : read.Error ?? "unreadable";
        }
        catch (Exception ex) when (IsHmiFailure(ex)) { liveError = ex.Message; }

        string? fileError = null;
        foreach (TimeSpan delay in ReadBackDelays.DefaultIfEmpty(TimeSpan.Zero))
        {
            if (delay > TimeSpan.Zero) Thread.Sleep(delay);
            var (symbols, path, error) = HmiInternalSymbols.ReadServerSymbols(project.ProjectDirectory);
            fileError = error;
            HmiServerSymbolEntry? entry = symbols?.FirstOrDefault(s => s.Name.Equals(mapName, StringComparison.Ordinal));
            if (entry is not null) return (entry, path, null);
        }
        return (null, "", $"live: {CondenseServerError(liveError ?? "")}; file: {fileError ?? "no entry"}");
    }
}
