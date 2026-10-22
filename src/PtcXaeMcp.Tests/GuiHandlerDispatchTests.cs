using System.Text.RegularExpressions;

namespace PtcXaeMcp.Tests;

/// <summary>
/// GUI-side counterpart of <see cref="McpToolResultContractTests"/>'s
/// dispatcher checks (issue #154): every method body in
/// <c>Form1*.cs</c> and the views under <c>Views/</c> (issue #270) that touches the live TwinCAT
/// session — directly (<c>_session.</c>) or through a
/// <c>new *Coordinator(_session</c> constructor call — must also call
/// <c>RunGuiComActionAsync</c> (or acquire the same <c>Dispatcher</c> lock
/// directly), or be named in <see cref="Exemptions"/> with a reason.
/// <para>
/// Method bodies are located by a source-text scan (brace matching from each
/// method signature), not reflection — WinForms event handlers are private
/// instance methods with no attribute to reflect on, unlike the MCP tool
/// surface. Coarse, like its MCP-side counterpart: a method that reaches
/// the session only through a helper it calls (e.g. <c>RunIbnAsync</c>) is not
/// itself flagged, because the flag looks for
/// <c>_session.</c>/<c>Coordinator(_session</c> text in ITS OWN body.
/// </para>
/// </summary>
public sealed class GuiHandlerDispatchTests
{
    /// <summary>
    /// Methods that legitimately touch the session outside
    /// <c>RunGuiComActionAsync</c>. Keep this list tiny — it is the
    /// race-condition exposure this test exists to bound.
    /// </summary>
    private static readonly Dictionary<string, string> Exemptions = new(StringComparer.Ordinal)
    {
        ["UpdateEnvironmentStatus"] =
            "the environment status probe (issue #154's UI-refresh path for the header chips, called " +
            "from Load, the header menu's 'Refresh status' and every MCP refresh request) stays " +
            "lock-free and synchronous by design, matching the twincat_get_status MCP exemption: a " +
            "single bounded COM read, not a mutating action.",
        ["TryDescribeResolvedHost"] =
            "read-only helper for UpdateEnvironmentStatus's status line (see above); same exemption.",
        ["DismissSecurityDialogFromGui"] =
            "pure Win32 dialog automation (issue #150, C-6), header menu: answers the security-code " +
            "dialog a running twincat_activate_config is blocked on, so it must not wait for the lock " +
            "that call holds. Matches twincat_dismiss_security_dialog's DispatcherExemptions entry.",
        ["ForceCloseXaeFromGui"] =
            "header-menu recovery action that kills the XAE process itself; must not serialize behind the very " +
            "dispatcher lock a hung operation may be holding. Matches twincat_force_close_xae's " +
            "DispatcherExemptions entry.",
    };

    private static readonly Regex MethodSignatureRegex = new(
        @"(?:public|private|protected|internal)\s+(?:static\s+)?(?:async\s+)?" +
        @"(?:void|Task(?:<[^)]*?>)?|string|bool|int)\??\s+(\w+)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex ConstructorRegex = new(
        @"(?:public|private)\s+(Form1)\s*\(\)\s*",
        RegexOptions.Compiled);

    [Fact]
    public void EverySessionTouchingMethod_UsesTheDispatcher_OrIsExplicitlyExempted()
    {
        var methods = EnumerateMethodBodies().ToList();

        static bool DirectlyDispatched(string body) =>
            body.Contains("RunGuiComActionAsync(", StringComparison.Ordinal)
            || body.Contains("_guiDispatch.RunAsync(", StringComparison.Ordinal)
            || body.Contains("Dispatcher.TryAcquire(", StringComparison.Ordinal);

        // A view-wiring method (e.g. ApprovalsView.Attach) commonly
        // defines its click handler as an inline lambda passed to a small
        // per-view wrapper (e.g. RunIbnAsync) that
        // itself calls RunGuiComActionAsync — so the OUTER method's own body
        // contains "_session." (inside the lambda argument) without containing
        // "RunGuiComActionAsync(" literally. Treat a call to any method that is
        // itself directly dispatched as dispatched too, one level deep.
        var wrapperNames = new HashSet<string>(
            methods.Where(m => DirectlyDispatched(m.Body)).Select(m => m.Name),
            StringComparer.Ordinal);

        var failures = new List<string>();
        foreach (var (file, name, body) in methods)
        {
            if (Exemptions.ContainsKey(name))
                continue;

            bool touchesSession = body.Contains("_session.", StringComparison.Ordinal)
                || Regex.IsMatch(body, @"new\s+[\w.]*Coordinator\(_session");
            if (!touchesSession)
                continue;

            bool dispatched = DirectlyDispatched(body)
                || wrapperNames.Any(w => w != name && body.Contains(w + "(", StringComparison.Ordinal));
            if (!dispatched)
                failures.Add($"{Path.GetFileName(file)}: {name}() touches the session without " +
                             "RunGuiComActionAsync/Dispatcher.TryAcquire (directly or via a dispatched " +
                             "wrapper method) and is not in Exemptions.");
        }

        Assert.True(failures.Count == 0, "GUI handlers bypassing the dispatcher lock:\n" + string.Join("\n", failures));
    }

    [Fact]
    public void Scan_CoversTheViews()
    {
        // The IBN Live actions moved from Form1.IbnLive.cs into Views/ApprovalsView.cs (#270);
        // a scan that silently skipped the views would pass the dispatcher check vacuously.
        var names = EnumerateMethodBodies().Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("ArmIbnFromGuiAsync", names);
        Assert.Contains("RunIbnAsync", names);
        Assert.Contains("UpdateEnvironmentStatus", names);
    }

    [Fact]
    public void Exemptions_ReferenceMethodsThatActuallyExist()
    {
        var found = new HashSet<string>(EnumerateMethodBodies().Select(m => m.Name), StringComparer.Ordinal);
        var stale = Exemptions.Keys.Where(name => !found.Contains(name)).ToList();
        Assert.True(stale.Count == 0,
            "GuiHandlerDispatchTests.Exemptions references methods that no longer exist: " + string.Join(", ", stale));
    }

    // ---- scanning helpers ---------------------------------------------------

    private static IEnumerable<(string File, string Name, string Body)> EnumerateMethodBodies()
    {
        // "Form1*.cs" (not "Form1.*.cs") — the latter requires a second dot
        // and would silently skip the bare Form1.cs itself.
        string root = FindWinFormsSourceDirectory();
        string views = Path.Combine(root, "Views");
        var files = Directory.EnumerateFiles(root, "Form1*.cs")
            .Concat(Directory.Exists(views) ? Directory.EnumerateFiles(views, "*.cs") : []);
        foreach (var file in files)
        {
            if (file.EndsWith("Form1.Designer.cs", StringComparison.OrdinalIgnoreCase))
                continue;

            var text = File.ReadAllText(file);
            foreach (var m in ExtractMethods(text))
                yield return (file, m.Name, m.Body);
        }
    }

    private static IEnumerable<(string Name, string Body)> ExtractMethods(string text)
    {
        foreach (Match m in MethodSignatureRegex.Matches(text))
        {
            if (TryExtractBody(text, m.Index, m.Value.LastIndexOf('(') + m.Index, out var body))
                yield return (m.Groups[1].Value, body);
        }
        foreach (Match m in ConstructorRegex.Matches(text))
        {
            int openParen = text.IndexOf('(', m.Index);
            if (TryExtractBody(text, m.Index, openParen, out var body))
                yield return (m.Groups[1].Value, body);
        }
    }

    /// <summary>
    /// From the index of an already-matched "(" (the parameter list open
    /// paren), finds the matching ")" by paren counting, then the next "{"
    /// after it, then that brace's matching "}" by brace counting. Returns
    /// false (rather than throwing) if the source doesn't parse the way this
    /// coarse scanner expects — e.g. a property or field declaration the
    /// signature regex accidentally matched.
    /// </summary>
    private static bool TryExtractBody(string text, int signatureStart, int openParenIndex, out string body)
    {
        body = string.Empty;
        int i = openParenIndex;
        if (i < 0 || i >= text.Length || text[i] != '(') return false;

        int parenDepth = 0;
        for (; i < text.Length; i++)
        {
            if (text[i] == '(') parenDepth++;
            else if (text[i] == ')')
            {
                parenDepth--;
                if (parenDepth == 0) { i++; break; }
            }
        }
        if (parenDepth != 0) return false;

        // Skip whitespace/newlines (and a possible expression-bodied "=>"
        // signature this regex isn't meant to match — bail if we hit one).
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        if (i >= text.Length || text[i] != '{') return false;

        int braceDepth = 0;
        int bodyStart = i;
        for (; i < text.Length; i++)
        {
            if (text[i] == '{') braceDepth++;
            else if (text[i] == '}')
            {
                braceDepth--;
                if (braceDepth == 0) { i++; break; }
            }
        }
        if (braceDepth != 0) return false;

        body = text[bodyStart..i];
        return true;
    }

    private static string FindWinFormsSourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 12 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "PtcXaeMcp.WinForms");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "Form1.cs")))
                return candidate;
        }

        throw new DirectoryNotFoundException(
            "Could not locate src/PtcXaeMcp.WinForms by walking up from " +
            AppContext.BaseDirectory + " — repo layout changed?");
    }
}
