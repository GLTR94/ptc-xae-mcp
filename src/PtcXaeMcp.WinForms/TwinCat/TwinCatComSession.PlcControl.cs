#if TWINCAT
using TwinCAT.Ads;
#endif
using System.Runtime.InteropServices;

namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed partial class TwinCatComSession
{
    // ---- plc-run-control --------------------------------------------------------
    //
    // After twincat_login with mode=download the runtime sits in STOP and there
    // was no way to start it from here: restart_runtime would reload the OLD boot
    // project and throw the freshly downloaded code away. The working path (proven
    // by hand with pyads) is the ADS state machine: WriteControl(AdsState.Run) on
    // the PLC runtime port. That leaves the downloaded program in place.
    //
    // Both directions read the state back afterwards and report it, so "started"
    // means the runtime answered RUN — not merely that the command was accepted.

#if !TWINCAT
    public PlcRunControlResult PlcStart(string? amsNetId, int adsPort)
        => new(false, null, null, amsNetId ?? "local", adsPort, TwinCatNotBuilt.Message);

    public PlcRunControlResult PlcStop(string? amsNetId, int adsPort)
        => new(false, null, null, amsNetId ?? "local", adsPort, TwinCatNotBuilt.Message);
#else

    public PlcRunControlResult PlcStart(string? amsNetId, int adsPort) =>
        SetPlcRunState(amsNetId, adsPort, AdsState.Run, "start", "started");

    public PlcRunControlResult PlcStop(string? amsNetId, int adsPort) =>
        SetPlcRunState(amsNetId, adsPort, AdsState.Stop, "stop", "stopped");

    // Past tense is passed in rather than derived: "stop" + "ed" produced
    // "stoped" in the live result text.
    private static PlcRunControlResult SetPlcRunState(
        string? amsNetId, int adsPort, AdsState target, string verb, string verbPast)
    {
        string resolvedNetId = ResolveAmsNetId(amsNetId);
        try
        {
            using var client = new AdsClient();
            client.Connect(resolvedNetId, adsPort);

            StateInfo before = client.ReadState();
            if (before.AdsState == target)
                return new(true, before.AdsState.ToString(), before.AdsState.ToString(),
                    resolvedNetId, adsPort, $"PLC is already {target}; nothing to {verb}.");

            // DeviceState is carried over unchanged — it is the device's own
            // sub-state, not ours to reinterpret.
            client.WriteControl(new StateInfo(target, before.DeviceState));

            // The transition is not instantaneous; poll briefly rather than
            // reading once and reporting a stale state.
            StateInfo after = before;
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                after = client.ReadState();
                if (after.AdsState == target) break;
                Thread.Sleep(100);
            }

            bool ok = after.AdsState == target;
            return new(ok, before.AdsState.ToString(), after.AdsState.ToString(),
                resolvedNetId, adsPort,
                ok
                    ? $"PLC {verbPast}: {before.AdsState} → {after.AdsState}."
                    : $"[Error] PLC did not reach {target} within 5 s — still {after.AdsState}. " +
                      "The command was accepted but the runtime did not change state (no program loaded, or it faulted).");
        }
        catch (Exception ex) when (ComCall.NotBusy(ex)) {
            return new(false, null, null, resolvedNetId, adsPort,
                $"[Error] PLC {verb} failed on {resolvedNetId}:{adsPort}: {ex.Message}");
        }
    }
#endif

    // ---- boot-project -----------------------------------------------------------
    //
    // "Activate Boot Project" is what makes a download survive a power cycle.
    // Without it, a plant that was downloaded to at 15:00 comes back up on
    // whatever boot project was last written — a silent, real-world data loss.
    //
    // The Automation Interface member that performs this is NOT something the
    // published docs pin down for this TC3 version, and guessing a COM member and
    // then reporting success is exactly the failure mode this repo has been bitten
    // by before. So: probe a candidate list against the PLC project node via
    // IDispatch::GetIDsOfNames (which resolves a name WITHOUT invoking it) and
    // report precisely what exists. dryRun (the default) stops there — it is safe
    // to run against a live plant because it changes nothing. Only dryRun=false
    // actually invokes the member that resolved.

    private static readonly string[] BootProjectMemberCandidates =
    [
        "ActivateBootProject",
        "GenerateBootProject",
        "CreateBootProject",
        "SaveBootProject",
        "BootProjectActivate",
    ];

    public BootProjectResult ActivateBootProject(bool dryRun) => ComBusyRetry.Run(() =>
    {
        dynamic dte = GetActiveTcDte();
        dynamic? plcProj = FindPlcSourceProjectNode(dte);
        if (plcProj is null)
            return new BootProjectResult(false, dryRun, null, Array.Empty<string>(),
                "[Error] No PLC source-project node found under TIPC — open a solution first.");

        var probed = new List<string>();
        string? resolved = null;
        object? resolvedTarget = null;

        // Probe EVERY plausible COM target, not just the source-project node:
        // ITcPlcProject/ITcPlcIECProject live on different nodes than the
        // ItemType-600 source project, and the system manager is a target in its
        // own right. An earlier pass checked only the source-project node and
        // wrongly concluded the Automation Interface had nothing to offer.
        // Explicit type: the call is dynamically bound (plcProj is dynamic), so
        // its result must be pinned down before it can be deconstructed.
        List<(string Label, object? Target)> comTargets = BootProjectComTargets(dte, plcProj!);
        foreach (var (label, target) in comTargets)
        {
            if (target is null) continue;

            // First: what does this object actually expose? Enumerating beats
            // guessing — and any member with "boot" in it is reported verbatim.
            var members = ComMemberProbe.ListMembers(target);
            var bootish = members
                .Where(m => m.IndexOf("boot", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();
            probed.Add(members.Count == 0
                ? $"{label}: no type info (cannot enumerate)"
                : $"{label}: {members.Count} members, boot-related: " +
                  (bootish.Count > 0 ? string.Join(", ", bootish) : "none"));

            // Prefer a real method over a property: BootProjectAutostart and
            // BootProjectEncryption are settings, GenerateBootProject is the action.
            foreach (var name in bootish.Concat(BootProjectMemberCandidates).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!ComMemberProbe.HasMember(target, name)) continue;
                probed.Add($"  → {label}.{name} resolves");
                bool isAction = name.IndexOf("generate", StringComparison.OrdinalIgnoreCase) >= 0
                             || name.IndexOf("activate", StringComparison.OrdinalIgnoreCase) >= 0
                             || name.IndexOf("create", StringComparison.OrdinalIgnoreCase) >= 0
                             || name.IndexOf("save", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isAction && resolved is null) { resolved = name; resolvedTarget = target; }
            }
        }

        // No documented COM member matched (verified live 2026-09-25: all five
        // candidates absent). The action DOES exist as a Solution-Explorer CONTEXT
        // MENU command — found by enumerating dte.Commands, the same technique that
        // pinned down the Error List clear. Context-menu commands act on the
        // SELECTED node, so the command reads as "disabled" until the PLC project
        // node is selected; that is why selection happens before the availability
        // check rather than after.
        // dte is dynamic, so the out variable needs an explicit type here.
        List<string> commandProbe;
        string? bootCommand = FindBootProjectCommand(dte, out commandProbe);
        probed.AddRange(commandProbe);

        if (resolved is null && bootCommand is null)
            return new BootProjectResult(false, dryRun, null, probed,
                "[Error] Neither a COM member nor a DTE command for the boot project was found on this " +
                "XAE. Activate it from the XAE menu (PLC → Activate Boot Project) and report this output.");

        // A real Automation-Interface method beats the GUI command: it needs no
        // node selection, no context menu and no IsAvailable guesswork, and it is
        // the only route that can work while the XAE is NOT logged in.
        // Live-enumerated on TC3 4026.22: GenerateBootProject / BootProjectAutostart
        // / BootProjectEncryption sit on the PARENT TIPC node (ITcPlcProject) — an
        // earlier pass probed only the ItemType-600 source project and therefore
        // wrongly concluded the interface offered nothing.
        if (resolved is not null && resolvedTarget is not null)
        {
            if (dryRun)
                return new BootProjectResult(true, true, resolved, probed,
                    $"Dry run only — nothing was changed. Automation Interface exposes '{resolved}'; " +
                    "re-run with dryRun=false to invoke it. This path does not depend on the Solution " +
                    "Explorer selection or on being logged in.");

            // Snapshot the boot artifacts BEFORE, so the effect can be proven
            // rather than asserted. "Invoked without throwing" is not the same as
            // "a boot project was written", and this tool family has been burned
            // by exactly that difference before.
            Dictionary<string, DateTime> bootBefore = SnapshotBootArtifacts(dte);

            try { ComMemberProbe.InvokeNoArgs(resolvedTarget, resolved); }
            catch (Exception ex) when (ComCall.NotBusy(ex)) {
                return new BootProjectResult(false, false, resolved, probed,
                    $"[Error] '{resolved}' resolved but failed when invoked: {ex.Message}");
            }

            SaveAll();

            Dictionary<string, DateTime> bootAfter = SnapshotBootArtifacts(dte);
            var rewritten = bootAfter
                .Where(a => !bootBefore.TryGetValue(a.Key, out var before) || before != a.Value)
                .Select(a => a.Key)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var f in rewritten.Take(12))
                probed.Add($"rewritten: {f}");

            // The PLC boot application itself is the file that decides what comes
            // up after a power cycle. A run that rewrites only side files (target
            // description, config zip) has NOT secured the program.
            bool appWritten = rewritten.Any(f =>
                f.EndsWith(".app", StringComparison.OrdinalIgnoreCase));

            if (rewritten.Count == 0)
                return new BootProjectResult(false, false, resolved, probed,
                    $"[Error] '{resolved}' ran without error but NOTHING in the _Boot folder changed — " +
                    "no boot project was written. The target would still come up on the previous one.");

            return new BootProjectResult(appWritten, false, resolved, probed,
                appWritten
                    ? $"Boot project written via '{resolved}': {rewritten.Count} artifact(s) rewritten, " +
                      "including the PLC boot application (*.app). The current program will survive a power cycle."
                    : $"[Error] '{resolved}' rewrote {rewritten.Count} file(s) but NOT a PLC boot application " +
                      "(*.app) — the program itself was not secured. Check the target platform folder under _Boot.");
        }

        // Fallback: the Solution-Explorer context-menu command. Acts on the
        // SELECTED node, so try each plausible one and report which arms it.
        bool commandEnabled = false;
        string? enablingNode = null;
        if (bootCommand is not null)
        {
            IntPtr hwnd = TryGetXaeMainWindow(dte);
            foreach (string candidate in BootProjectNodeCandidates(plcProj!))
            {
                bool selected = SolutionExplorerAutomation.TrySelectNode(hwnd, candidate, out string selectNote);
                bool avail = false;
                if (selected)
                {
                    try { avail = (bool)dte.Commands.Item(bootCommand, 0).IsAvailable; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
                }
                probed.Add($"Select '{candidate}': {(selected ? "ok" : $"failed ({selectNote})")}" +
                           $" → {bootCommand} {(avail ? "AVAILABLE" : "disabled")}");
                if (avail) { commandEnabled = true; enablingNode = candidate; break; }
            }
        }
        if (enablingNode is not null)
            probed.Add($"Command is armed with '{enablingNode}' selected.");

        if (dryRun)
        {
            string what = bootCommand is not null
                ? $"No Automation-Interface method found; falling back to DTE command '{bootCommand}'" +
                  (commandEnabled
                    ? " which is available with the PLC project node selected."
                    : " which reports as disabled — invoking it may do nothing.")
                : "nothing usable found.";
            return new BootProjectResult(commandEnabled, true, bootCommand, probed,
                $"Dry run only — nothing was changed. {what}");
        }

        if (bootCommand is not null)
        {
            // Only reached when the Automation Interface offered nothing. IsAvailable
            // is not trustworthy for OtherContextMenus.* commands (verified live:
            // false for every candidate node even while logged in), so this refuses
            // rather than firing blindly — the COM path above is the real answer.
            if (!commandEnabled)
                return new BootProjectResult(false, false, bootCommand, probed,
                    $"[Error] '{bootCommand}' reports as disabled for every candidate node, so invoking " +
                    "it may do nothing — refusing by default rather than reporting a false success. " +
                    "IsAvailable is known to be unreliable for context-menu commands; if the action IS " +
                    "possible in the XAE right now, re-run with forceEvenIfDisabled=true.");
            try { dte.ExecuteCommand(bootCommand); }
            catch (Exception ex) when (ComCall.NotBusy(ex)) {
                return new BootProjectResult(false, false, bootCommand, probed,
                    $"[Error] '{bootCommand}' failed when invoked: {ex.Message}");
            }
            return new BootProjectResult(true, false, bootCommand, probed,
                $"Invoked '{bootCommand}'{(commandEnabled ? "" : " (forced: it reported as disabled)")}. " +
                "This tool cannot read the boot-project timestamp back — " +
                "confirm in the XAE (or in the target's Boot folder) that it was written.");
        }

        try
        {
            ComMemberProbe.InvokeNoArgs(plcProj!, resolved!);
        }
        catch (Exception ex) when (ComCall.NotBusy(ex)) {
            return new BootProjectResult(false, false, resolved, probed,
                $"[Error] '{resolved}' resolved but failed when invoked: {ex.Message}");
        }

        SaveAll();
        return new BootProjectResult(true, false, resolved, probed,
            $"Invoked '{resolved}' on the PLC project node and saved. " +
            "This tool cannot read back the boot-project timestamp — confirm in the XAE " +
            "(or in the target's Boot folder) that it was written.");
    }, OpenTimeout);

    // Timestamps of every file under the solution's _Boot folder. This is the
    // evidence that a boot project was really written: TwinCAT puts the boot
    // application at _Boot/<target platform>/Plc/Port_<n>.app, and there can be
    // SEVERAL platform folders side by side (e.g. "TwinCAT RT (x64)" next to a
    // stale "TwinCAT OS (x64)"). Looking at only one of them is how a successful
    // run gets misread as a failure — which happened during development.
    private static Dictionary<string, DateTime> SnapshotBootArtifacts(dynamic dte)
    {
        var map = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        try
        {
            string slnPath = (string)dte.Solution.FullName;
            string? dir = Path.GetDirectoryName(slnPath);
            if (string.IsNullOrEmpty(dir)) return map;

            foreach (var bootDir in Directory.EnumerateDirectories(dir, "_Boot", SearchOption.AllDirectories))
            {
                foreach (var f in Directory.EnumerateFiles(bootDir, "*", SearchOption.AllDirectories))
                {
                    try { map[Path.GetRelativePath(dir, f)] = File.GetLastWriteTimeUtc(f); }
                    catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
                }
            }
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { /* no _Boot folder — the caller reports "nothing changed" */ }
        return map;
    }

    // Every COM object that could plausibly carry the boot-project member.
    // The TwinCAT Automation Interface spreads PLC functionality across several
    // objects: the ItemType-600 source project (ITcPlcIECProject), its parent
    // TIPC node (ITcPlcProject — where boot-project members are documented), the
    // runtime instance child, the nested project object, and ITcSysManager.
    private static List<(string Label, object? Target)> BootProjectComTargets(dynamic dte, dynamic plcProj)
    {
        var targets = new List<(string, object?)>();

        void Add(string label, Func<object?> get)
        {
            try { targets.Add((label, get())); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { targets.Add((label, null)); }
        }

        Add("source project (ItemType 600)", () => plcProj);
        Add("parent TIPC node", () => plcProj.Parent);
        Add("parent.NestedProject", () => plcProj.Parent?.NestedProject);
        Add("system manager", () => TryGetSystemManager(dte));

        // Children of the parent — the runtime instance lives here.
        try
        {
            dynamic parent = plcProj.Parent;
            int count = (int)parent.ChildCount;
            for (int i = 1; i <= count; i++)
            {
                dynamic child;
                try { child = parent.Child(i); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { continue; }
                string cn = string.Empty;
                try { cn = (string)child.Name ?? string.Empty; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
                targets.Add(($"child '{cn}'", (object)child));
            }
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }

        return targets;
    }

    // Node names to try, nearest first: the PLC source-project node itself, then
    // its parent (the TIPC PLC node), then any child of that parent that looks
    // like the runtime instance — in TwinCAT the boot project belongs to the
    // instance, not to the source project, so the owning node is most likely one
    // of the latter two.
    private static List<string> BootProjectNodeCandidates(dynamic plcProj)
    {
        var names = new List<string>();

        void Add(string? n)
        {
            if (!string.IsNullOrWhiteSpace(n) && !names.Contains(n!, StringComparer.OrdinalIgnoreCase))
                names.Add(n!);
        }

        try { Add((string)plcProj.Name); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }

        dynamic? parent = null;
        try { parent = plcProj.Parent; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
        if (parent is not null)
        {
            try { Add((string)parent.Name); } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
            int count = 0;
            try { count = (int)parent.ChildCount; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
            for (int i = 1; i <= count; i++)
            {
                try
                {
                    dynamic child = parent.Child(i);
                    string cn = (string)child.Name ?? string.Empty;
                    if (cn.IndexOf("instan", StringComparison.OrdinalIgnoreCase) >= 0)
                        Add(cn);
                }
                catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
            }
        }
        return names;
    }

    // Discovers the "activate boot project" context-menu command at runtime rather
    // than hardcoding a locale-specific name: this XAE is German
    // ("OtherContextMenus.Projekte.BootProjektaktivieren"), an English one differs,
    // and the ClearErrorList comment already records how a hardcoded German leaf
    // breaks elsewhere. Excludes the neighbours that are NOT this action:
    // "Autostart Boot Projekt" (a setting) and EtherCAT "BOOTSTRAP" diagnostics.
    private static string? FindBootProjectCommand(dynamic dte, out List<string> probe)
    {
        probe = new List<string>();
        string? best = null;
        try
        {
            foreach (dynamic cmd in dte.Commands)
            {
                string name;
                try { name = (string)cmd.Name ?? string.Empty; }
                catch (Exception comEx) when (ComCall.NotBusy(comEx)) { continue; }
                if (string.IsNullOrEmpty(name)) continue;
                if (name.IndexOf("boot", StringComparison.OrdinalIgnoreCase) < 0) continue;

                bool avail;
                try { avail = (bool)cmd.IsAvailable; } catch (Exception comEx) when (ComCall.NotBusy(comEx)) { avail = false; }
                probe.Add($"DTE command {name}: {(avail ? "available" : "disabled")}");

                if (name.IndexOf("autostart", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (name.IndexOf("bootstrap", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                bool looksLikeActivate =
                    name.IndexOf("aktivier", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("activate", StringComparison.OrdinalIgnoreCase) >= 0;
                if (looksLikeActivate) best ??= name;
            }
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { probe.Add("DTE command enumeration unavailable"); }
        if (probe.Count == 0) probe.Add("DTE command *boot*: none found");
        return best;
    }
}

// Resolves a COM member by name WITHOUT calling it, so a caller can find out
// whether a member exists before deciding to invoke something irreversible.
// dynamic dispatch cannot do this — it only tells you after the call already
// happened.
internal static class ComMemberProbe
{
    [ComImport, Guid("00020400-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDispatch
    {
        [PreserveSig] int GetTypeInfoCount(out int count);
        [PreserveSig] int GetTypeInfo(int index, int lcid, out IntPtr typeInfo);
        [PreserveSig] int GetIDsOfNames(
            ref Guid riid,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] names,
            int cNames, int lcid,
            [MarshalAs(UnmanagedType.LPArray)] int[] dispIds);
        // Invoke is deliberately NOT declared: this type must not be able to call.
    }

    private const int DISP_E_UNKNOWNNAME = unchecked((int)0x80020006);

    public static bool HasMember(object comObject, string memberName)
    {
        if (comObject is not IDispatch disp) return false;
        var iidNull = Guid.Empty;
        var dispIds = new int[1];
        try
        {
            int hr = disp.GetIDsOfNames(ref iidNull, [memberName], 1, 0, dispIds);
            if (hr == DISP_E_UNKNOWNNAME) return false;
            return hr >= 0;
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { return false; }
    }

    // Enumerates the member names a COM object actually exposes, via its type
    // information. Guessing names and concluding "absent" is how a whole
    // interface gets written off by mistake — this answers the question instead
    // of probing a hand-written list.
    public static IReadOnlyList<string> ListMembers(object comObject)
    {
        var names = new List<string>();
        if (comObject is not IDispatch disp) return names;

        IntPtr pTypeInfo = IntPtr.Zero;
        try
        {
            if (disp.GetTypeInfoCount(out int count) != 0 || count == 0) return names;
            if (disp.GetTypeInfo(0, 0, out pTypeInfo) != 0 || pTypeInfo == IntPtr.Zero) return names;

            var typeInfo = (System.Runtime.InteropServices.ComTypes.ITypeInfo)
                Marshal.GetObjectForIUnknown(pTypeInfo);

            typeInfo.GetTypeAttr(out IntPtr pAttr);
            if (pAttr == IntPtr.Zero) return names;
            try
            {
                var attr = Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.TYPEATTR>(pAttr);
                for (int i = 0; i < attr.cFuncs; i++)
                {
                    IntPtr pFunc = IntPtr.Zero;
                    try
                    {
                        typeInfo.GetFuncDesc(i, out pFunc);
                        if (pFunc == IntPtr.Zero) continue;
                        var fd = Marshal.PtrToStructure<System.Runtime.InteropServices.ComTypes.FUNCDESC>(pFunc);
                        typeInfo.GetDocumentation(fd.memid, out string? name, out _, out _, out _);
                        if (!string.IsNullOrEmpty(name) && !names.Contains(name!, StringComparer.Ordinal))
                            names.Add(name!);
                    }
                    catch (Exception comEx) when (ComCall.NotBusy(comEx)) { }
                    finally { if (pFunc != IntPtr.Zero) typeInfo.ReleaseFuncDesc(pFunc); }
                }
            }
            finally { typeInfo.ReleaseTypeAttr(pAttr); }
        }
        catch (Exception comEx) when (ComCall.NotBusy(comEx)) { /* no type info — caller falls back to name probing */ }
        finally { if (pTypeInfo != IntPtr.Zero) Marshal.Release(pTypeInfo); }

        return names;
    }

    public static void InvokeNoArgs(object comObject, string memberName) =>
        comObject.GetType().InvokeMember(
            memberName,
            System.Reflection.BindingFlags.InvokeMethod,
            binder: null, target: comObject, args: null,
            culture: System.Globalization.CultureInfo.InvariantCulture);
}
