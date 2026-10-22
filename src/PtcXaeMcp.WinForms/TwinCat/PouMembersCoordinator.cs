namespace PtcXaeMcp.WinForms.TwinCat;

// Orchestrates the feature/pou-handling tools: structured POU members
// (list/add/read/write), object lifecycle (delete/rename), and PLCopen
// interchange (export/import).
//
// Same shape as PlcCodeCoordinator: each method does cheap input validation
// first, then SessionGuards.RequireOpenSolution, then delegates to the session
// and turns exceptions into "[Error] ..." summaries. WritePouMember reuses the
// ST-only refusal contract from WritePouBody.
internal sealed class PouMembersCoordinator(ITwinCatSession session)
{
    // METHOD is supported again (issue #19): the earlier TC3-4026.22
    // "Name mismatch" on a freshly-created FB was caused by the multi-element
    // {lang, ret} string-array vInfo; the session now adds methods with the
    // scalar language-string vInfo (the shape the working ACTION path uses,
    // and the documented Infosys default), with documented fallbacks.
    /// <summary>How long the PLCopen import read-back waits for the imported objects to appear. Test seam.</summary>
    internal TimeSpan ImportSettleTimeout { get; init; } = TimeSpan.FromSeconds(15);

    private static readonly string[] AddableMemberKinds =
        [PlcMemberKind.Method, PlcMemberKind.Action, PlcMemberKind.Property,
         PlcMemberKind.Get, PlcMemberKind.Set];

    public (string Summary, IReadOnlyList<PouMemberEntry> Members) ListPouMembers(string pouPath)
    {
        if (string.IsNullOrWhiteSpace(pouPath))
            return ("[Error] pouPath is required.", Array.Empty<PouMemberEntry>());

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return (error, Array.Empty<PouMemberEntry>());

        IReadOnlyList<PouMemberEntry> members;
        try { members = session.ListPouMembers(pouPath); }
        catch (Exception ex) { return ($"[Error] {ex.Message}", Array.Empty<PouMemberEntry>()); }

        if (members.Count == 0)
            return ($"POU '{pouPath}' has no METHOD/ACTION/PROPERTY members.", members);

        int methods = members.Count(m => m.Kind == PlcMemberKind.Method);
        int actions = members.Count(m => m.Kind == PlcMemberKind.Action);
        int props = members.Count(m => m.Kind == PlcMemberKind.Property);
        return ($"Found {members.Count} member{(members.Count == 1 ? "" : "s")} " +
                $"({methods} METHOD, {actions} ACTION, {props} PROPERTY) in '{pouPath}'.",
                members);
    }

    public string AddPouMember(string pouPath, string kind, string name, string? returnType, string? implLanguage,
        string? accessors = null) =>
        AddPouMember(pouPath, kind, name, returnType, implLanguage, accessors, out _);

    /// <summary>As the legacy overload; <paramref name="createdPath"/> is the new member's tree path on success, else null.</summary>
    internal string AddPouMember(string pouPath, string kind, string name, string? returnType, string? implLanguage,
        string? accessors, out string? createdPath)
    {
        createdPath = null;
        if (string.IsNullOrWhiteSpace(pouPath))
            return "[Error] pouPath is required.";
        if (string.IsNullOrWhiteSpace(kind))
            return "[Error] kind is required (ACTION | PROPERTY | GET | SET).";
        if (string.IsNullOrWhiteSpace(name))
            return "[Error] name is required.";

        string normKind = kind.Trim().ToUpperInvariant();
        if (!AddableMemberKinds.Contains(normKind))
            return $"[Error] Unknown kind '{kind}'. Expected METHOD, ACTION, PROPERTY, GET, or SET.";

        if (!PropertyAccessorsParsing.TryParse(accessors, out var accessorsValue, out var accessorsError))
            return accessorsError!;
        if (accessorsValue != PropertyAccessors.Both && normKind != PlcMemberKind.Property)
            return $"[Error] accessors is only meaningful for kind=PROPERTY (got kind={normKind}).";

        // METHOD/ACTION/PROPERTY carry a caller-chosen identifier; reject a
        // reserved/invalid one up front with a clear message instead of letting
        // it surface as TC3's misleading "Name mismatch" (issue #19). GET/SET
        // create fixed "Get"/"Set" nodes, so their name is not validated here.
        if (normKind is PlcMemberKind.Method or PlcMemberKind.Action or PlcMemberKind.Property)
        {
            var nameError = IecIdentifier.Validate(name, $"{normKind} name");
            if (nameError is not null) return nameError;
        }

        // Unknown language -> [Error], never a silent ST (issue #148, B-8). Validated before any
        // session touch so a typo costs neither a logout nor a half-created member.
        var langError = PlcImplLanguage.Validate(implLanguage);
        if (langError is not null) return langError;
        string normLang = PlcImplLanguage.Normalize(implLanguage);
        // returnType is only meaningful for METHOD/PROPERTY; default BOOL.
        string? normReturn = string.IsNullOrWhiteSpace(returnType) ? null : returnType!.Trim();
        // GET/SET create fixed "Get"/"Set" nodes: report the real node name, not the ignored input.
        string normName = normKind switch
        {
            PlcMemberKind.Get => "Get",
            PlcMemberKind.Set => "Set",
            _ => name.Trim(),
        };

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;
        error = SessionGuards.RequirePlcNode(session, pouPath, "POU");
        if (error is not null) return error;
        error = SessionGuards.EnsureOfflineForWrite(session);
        if (error is not null) return error;

        try
        {
            var path = session.AddPouMember(pouPath, normKind, normName, normReturn, normLang, accessorsValue);
            createdPath = path;
            string accessorsNote = normKind == PlcMemberKind.Property && accessorsValue != PropertyAccessors.Both
                ? $" ({accessorsValue.ToString().ToUpperInvariant()} only)"
                : "";
            return $"Added {normKind} '{normName}'{accessorsNote} at {path}.";
        }
        catch (Exception ex)
        {
            return $"[Error] {ex.Message}";
        }
    }

    public (string Summary, PouBody? Body) ReadPouMember(string memberPath)
    {
        if (string.IsNullOrWhiteSpace(memberPath))
            return ("[Error] memberPath is required.", null);

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return (error, null);

        try
        {
            var body = session.ReadPouMember(memberPath);
            return ($"Read member '{memberPath}' ({body.Language}).", body);
        }
        catch (Exception ex)
        {
            return ($"[Error] {ex.Message}", null);
        }
    }

    public string WritePouMember(string memberPath, string? declaration, string? implementation,
        string? declarationFile = null, string? implementationFile = null)
    {
        if (string.IsNullOrWhiteSpace(memberPath))
            return "[Error] memberPath is required.";

        var decl = SourceTextInput.Resolve("declaration", declaration, declarationFile);
        if (decl.Error is not null) return decl.Error;
        var impl = SourceTextInput.Resolve("implementation", implementation, implementationFile);
        if (impl.Error is not null) return impl.Error;
        declaration = decl.Text;
        implementation = impl.Text;

        if (declaration is null && implementation is null)
            return "[Error] At least one of declaration or implementation must be supplied — " +
                   "inline, or from a file via declarationFile/implementationFile.";

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        // Target read + ST-only check BEFORE the logout (issue #148, B-15): a wrong path or a
        // non-ST member must not cost a logout from a running plant.
        try
        {
            var existing = session.ReadPouMember(memberPath);
            if (!string.Equals(existing.Language, "ST", StringComparison.OrdinalIgnoreCase))
                return $"[Error] Member '{memberPath}' is implemented in {existing.Language}. Only ST members can be written through this tool.";
        }
        catch (Exception ex) { return McpErrors.EnsureError(ex.Message); }

        error = SessionGuards.EnsureOfflineForWrite(session);
        if (error is not null) return error;

        try
        {
            session.WritePouMember(memberPath, declaration, implementation);
            return $"Wrote member '{memberPath}'." + SourceTextInput.FormatNotes(decl, impl);
        }
        catch (Exception ex)
        {
            return $"[Error] {ex.Message}";
        }
    }

    public string DeletePlcObject(string treePath)
    {
        if (string.IsNullOrWhiteSpace(treePath))
            return "[Error] treePath is required.";

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        // Whitelist check BEFORE the logout (issue #148, LIVE-8 / B-4 / B-15): only PLC source
        // objects and their members are deletable, never the project root, the PLC instance,
        // References, a folder, or a node whose ItemType cannot be read.
        PlcNodeInfo node;
        try { node = session.ProbePlcNode(treePath); }
        catch (Exception ex) when (ComCall.IsBusy(ex)) { return McpErrors.EnsureError(ComCall.Describe(ex)); }
        catch (Exception ex) { return McpErrors.EnsureError(ex.Message); }
        var refusal = PlcDeletePolicy.Check(treePath, node);
        if (refusal is not null) return refusal;

        error = SessionGuards.EnsureOfflineForWrite(session);
        if (error is not null) return error;

        try { session.DeletePlcObject(treePath); }
        catch (Exception ex) { return $"[Error] {ex.Message}"; }

        // B-13 (issue #152): the delete is proven by an independent, re-resolved lookup — the
        // path must be gone. A DeleteChild that returns without effect is not a success.
        var gone = PlcTreeReadBack.RequireMissing(session, treePath,
            $"Delete of '{treePath}'");
        if (gone is not null) return gone;

        return $"Deleted {PlcDeletePolicy.KindName(node.ItemType!.Value)} '{treePath}' (read-back: the path no longer resolves). " +
               "(If it was referenced elsewhere, run twincat_build_plc to check the project still builds.)";
    }

    // folderName: a FOLDER may carry any file-system-safe name ("01_Application"), not only an IEC identifier.
    // updateReferences: how the XAE's "rename via refactoring" prompt is answered — false = "Nein" (default:
    // only the object is renamed), true = "Ja" (TwinCAT rewrites the references to the old name). The
    // result always says whether the prompt appeared and what was answered.
    public string RenamePlcObject(string treePath, string newName, bool folderName = false, bool updateReferences = false)
    {
        if (string.IsNullOrWhiteSpace(treePath))
            return "[Error] treePath is required.";
        if (string.IsNullOrWhiteSpace(newName))
            return "[Error] newName is required.";

        var nameError = folderName ? PlcFolderName.Validate(newName, "newName") : IecIdentifier.Validate(newName, "newName");
        if (nameError is not null) return nameError;

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;
        error = SessionGuards.RequirePlcNode(session, treePath, "PLC object");
        if (error is not null) return error;
        error = SessionGuards.EnsureOfflineForWrite(session);
        if (error is not null) return error;

        string newPath;
        string? prompt;
        bool promptFailed;
        try { (newPath, prompt, promptFailed) = session.RenamePlcObject(treePath, newName.Trim(), updateReferences); }
        catch (Exception ex) { return $"[Error] {ex.Message}"; }

        // B-13 (issue #152): re-resolve both paths — the new one must exist and (unless only the
        // case changed) the old one must be gone.
        var present = PlcTreeReadBack.RequirePresent(session, newPath,
            $"Rename of '{treePath}' to '{newName.Trim()}'");
        if (present is not null) return present + PromptSuffix(prompt);
        if (!string.Equals(newPath, treePath, StringComparison.OrdinalIgnoreCase))
        {
            var oldGone = PlcTreeReadBack.RequireMissing(session, treePath,
                $"Rename of '{treePath}' to '{newName.Trim()}'");
            if (oldGone is not null) return oldGone + PromptSuffix(prompt);
        }

        // The prompt was not answered as asked (updateReferences decides what TwinCAT does to the callers):
        // the rename landed, but not the way the caller requested.
        if (promptFailed)
            return $"[Error] Renamed '{treePath}' to '{newPath}', but {RefactoringNote(prompt, updateReferences, SettingIfSilent(prompt))}";

        return $"Renamed '{treePath}' to '{newPath}' (read-back: the new path resolves, the old one does not). " +
               RefactoringNote(prompt, updateReferences, SettingIfSilent(prompt));
    }

    // Only a silent rename (no prompt) depends on the refactoring option; reading the file is cheap and read-only.
    private static RenameRefactoringSetting SettingIfSilent(string? prompt) =>
        prompt is null ? PlcRefactoringOption.Probe() : RenameRefactoringSetting.Unknown;

    /// <summary>
    /// The result sentence about the XAE's "rename via refactoring" prompt — always present, so the caller
    /// knows whether TwinCAT asked and what was answered.
    /// </summary>
    internal static string RefactoringNote(string? prompt, bool updateReferences = false,
        RenameRefactoringSetting setting = RenameRefactoringSetting.Unknown) =>
        prompt is not null
            ? $"Refactoring prompt: {prompt}."
            // Issue #239 (decompiled): an Automation-Interface rename refactors SILENTLY when the XAE option
            // Refactoring > Navigator > "Beim Umbenennen von Objekten" is on, and renames only the element when off.
            : setting == RenameRefactoringSetting.Enabled
                ? updateReferences
                    ? "Refactoring prompt: none appeared. Callers renamed too: the XAE option 'Refactoring > Navigator > Beim Umbenennen von Objekten' is ON, so " +
                      "an Automation-Interface rename refactors silently (no prompt) - as requested by updateReferences=true."
                    : "Refactoring prompt: none appeared. updateReferences=false could NOT be enforced: the XAE option 'Refactoring > Navigator > Beim Umbenennen " +
                      "von Objekten' is ON, so an Automation-Interface rename refactors silently (no prompt) and the callers " +
                      "WERE renamed too. Turn the option off in the XAE (Tools > Options > TwinCAT > PLC Environment > " +
                      "Refactoring) to rename only the element."
            : setting == RenameRefactoringSetting.Disabled
                ? updateReferences
                    ? "Refactoring prompt: none appeared. updateReferences=true could NOT be honoured: the XAE option 'Refactoring > Navigator > Beim Umbenennen " +
                      "von Objekten' is OFF, so only the element was renamed and the callers still use the old name - " +
                      "rebuild (twincat_build_plc) and fix them, or turn the option on."
                    : "Refactoring prompt: none appeared. Callers untouched: the XAE option 'Refactoring > Navigator > Beim Umbenennen von Objekten' is OFF, so " +
                      "only the element was renamed - as requested by updateReferences=false."
            : updateReferences
                ? "Refactoring prompt: none appeared. The XAE may have updated the callers on its own or not at all — " +
                  "rebuild (twincat_build_plc) to check the callers of the old name."
                // Issue #239: on 4026.22 the XAE rewrote a caller WITHOUT asking, so "leave the callers" was
                // not enforceable. Say so instead of implying the callers still use the old name.
                : "Refactoring prompt: none appeared. updateReferences=false could NOT be enforced: the XAE may have " +
                  "renamed the callers on its own without asking (seen live on 4026.22, issue #239). Search the " +
                  "project for the old and the new name and rebuild before relying on the callers.";

    private static string PromptSuffix(string? prompt) => prompt is null ? "" : $" (refactoring prompt: {prompt})";

    public string PlcOpenExport(string filePath, string selector)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "[Error] filePath is required.";
        if (string.IsNullOrWhiteSpace(selector))
            return "[Error] selector is required (e.g. \"POUs.FB_Foo;POUs.MAIN\").";

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        try
        {
            var written = session.PlcOpenExport(filePath, selector.Trim());
            return $"Exported PLCopen XML for '{selector}' to {written}.";
        }
        catch (Exception ex)
        {
            return $"[Error] {ex.Message}";
        }
    }

    // Maps the conflictMode string to the numeric option PlcOpenImport expects.
    // Default is Skip (3) — non-destructive — when the caller omits the mode.
    public static (bool Ok, int Code, string? Error) MapConflictMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return (true, 3, null);
        return mode.Trim().ToUpperInvariant() switch
        {
            "NONE"    => (true, 0, null),
            "RENAME"  => (true, 1, null),
            "REPLACE" => (true, 2, null),
            "SKIP"    => (true, 3, null),
            _ => (false, -1,
                $"[Error] Unknown conflictMode '{mode}'. Expected None, Rename, Replace, or Skip."),
        };
    }

    public string PlcOpenImport(string filePath, string? conflictMode)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "[Error] filePath is required.";

        var (ok, code, modeError) = MapConflictMode(conflictMode);
        if (!ok) return modeError!;
        if (!File.Exists(filePath))
            return $"[Error] PLCopen file not found: {filePath}";

        // B-14 (issue #152): the objects the file carries, read before anything is touched — an
        // unreadable file or one without importable objects costs no logout.
        IReadOnlyList<string> fileObjects;
        try { fileObjects = PlcOpenImportCheck.ObjectNames(File.ReadAllText(filePath)); }
        catch (Exception ex) when (ex is System.Xml.XmlException or IOException or UnauthorizedAccessException)
        {
            return $"[Error] '{filePath}' is not a readable PLCopen XML file: {ex.Message}";
        }
        if (fileObjects.Count == 0)
            return $"[Error] '{filePath}' contains no POU, DUT, GVL or INTERFACE — nothing to import.";

        var error = SessionGuards.RequireOpenSolution(session);
        if (error is not null) return error;

        IReadOnlyList<PouEntry> before;
        try { before = session.ListPous().ToList(); }
        catch (Exception ex) { return McpErrors.EnsureError(ComCall.Describe(ex)); }

        error = SessionGuards.EnsureOfflineForWrite(session);
        if (error is not null) return error;

        string imported;
        try { imported = session.PlcOpenImport(filePath, code); }
        catch (Exception ex) { return $"[Error] {ex.Message}"; }

        // The imported objects appear in a separate tree walk only after a settle lag (live, TC 4026.22:
        // the renamed copies of conflictMode=Rename were missing from the first re-read) — poll.
        PlcOpenImportCheck.Outcome outcome;
        var deadline = DateTime.UtcNow + ImportSettleTimeout;
        while (true)
        {
            IReadOnlyList<PouEntry> after;
            try { after = session.ListPous().ToList(); }
            catch (Exception ex)
            {
                return McpErrors.EnsureError($"PLCopen import from {imported} was sent, but the PLC tree could not be " +
                                             $"re-read to confirm it: {ComCall.Describe(ex)}");
            }
            outcome = PlcOpenImportCheck.Evaluate(fileObjects, before, after, code);
            if (outcome.Settled || DateTime.UtcNow >= deadline) break;
            Thread.Sleep(500);
        }
        return outcome.Error is not null
            ? $"[Error] PLCopen import from {imported} did not land as expected: {outcome.Error} ({outcome.Summary})"
            : $"Imported PLCopen XML from {imported} (conflictMode={code}): {outcome.Summary} (read-back: PLC tree before/after).";
    }
}
