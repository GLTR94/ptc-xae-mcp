using System.Text;
using System.Windows.Automation;
using System.Globalization;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// GUI best-effort: reads the symbol-resolved call stack from the dump
/// currently loaded in the TwinCAT XAE IDE, using UIAutomation to find
/// editor tab captions that carry the "[Online]" suffix.
///
/// FRAGILE by design (spec §GUI best-effort). The foreground-lock issue,
/// tab caption format, and automation tree depth are all XAE-version
/// sensitive. Always degrades to a clear precondition error — never fabricates.
/// </summary>
internal static class CoreDumpCallStackReader
{
    private const string OnlineSuffix = "[Online]";

    public static string ReadCallStack()
    {
        var xaeProcs = System.Diagnostics.Process.GetProcessesByName("TcXaeShell");
        if (xaeProcs.Length == 0)
        {
            return "[Precondition not met] TwinCAT XAE (TcXaeShell) is not running.\n" +
                   "Load the core dump in the IDE first: PLC → Core Dump → Load core dump.";
        }

        try
        {
            return ScrapeOnlineTabs(xaeProcs);
        }
        catch (Exception ex)
        {
            return $"[Error] UIAutomation scrape failed: {ex.Message}\n" +
                   "Ensure the core dump is loaded in the IDE: PLC → Core Dump → Load core dump.";
        }
        finally
        {
            foreach (var p in xaeProcs) p.Dispose();
        }
    }

    private static string ScrapeOnlineTabs(System.Diagnostics.Process[] xaeProcs)
    {
        var onlineTabs = new List<string>();

        foreach (var proc in xaeProcs)
        {
            if (proc.HasExited) continue;
            IntPtr hwnd = proc.MainWindowHandle;
            if (hwnd == IntPtr.Zero) continue;

            AutomationElement? root;
            try { root = AutomationElement.FromHandle(hwnd); }
            catch { continue; }

            // Walk the automation tree looking for elements whose Name ends with [Online]
            var walker = TreeWalker.ContentViewWalker;
            CollectOnlineElements(root, walker, onlineTabs, depth: 0);
        }

        if (onlineTabs.Count == 0)
        {
            return "[Precondition not met] No [Online] editor tabs found in the TwinCAT XAE IDE.\n" +
                   "Load the core dump first: PLC → Core Dump → Load core dump.\n" +
                   "If the dump is already loaded, the IDE may need a moment to render the online view.";
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== Core dump call stack (best-effort GUI scrape) ===");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Found {onlineTabs.Count} [Online] tab(s) in XAE editor:");
        sb.AppendLine();
        foreach (var t in onlineTabs)
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {t}");
        sb.AppendLine();
        sb.AppendLine("[Note] This shows the POU/FB instances that were executing at crash time.");
        sb.AppendLine("       The top-most tab is the code location where the exception was raised.");
        sb.AppendLine("       To see the exact line: look for the highlighted active line in the editor.");
        sb.AppendLine("       Symbol-resolved variable values require the matching .compileinfo file.");
        return sb.ToString();
    }

    private static void CollectOnlineElements(
        AutomationElement element, TreeWalker walker, List<string> found, int depth)
    {
        if (depth > 8) return;  // avoid infinite walk on deep trees

        try
        {
            string name = element.Current.Name ?? "";
            if (name.EndsWith(OnlineSuffix, StringComparison.OrdinalIgnoreCase) &&
                !found.Contains(name))
            {
                found.Add(name);
            }
        }
        catch { return; }

        try
        {
            var child = walker.GetFirstChild(element);
            while (child != null)
            {
                CollectOnlineElements(child, walker, found, depth + 1);
                child = walker.GetNextSibling(child);
            }
        }
        catch { }
    }
}
