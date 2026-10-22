using System.Text;
using System.Globalization;

namespace PtcXaeMcp.WinForms.Mcp;

/// <summary>
/// Renders one run's activity — the same unified timeline the MCP Activity tab
/// shows (XAE operations grouped under the test that issued them) — as a single
/// self-contained, human-readable HTML document. This is the artefact the tab
/// links to: the raw per-run JSON-lines log stays the machine-readable record,
/// while this is the view a human actually opens.
/// <para>Pure function: no I/O, no GUI, no COM — straightforward to unit-test.</para>
/// </summary>
public static class McpActivityHtmlReport
{
    /// <summary>
    /// Builds the HTML for a run. <paramref name="entries"/> are the XAE
    /// operations (oldest first); <paramref name="boundaries"/> are the
    /// per-test markers, each carrying the operation count captured when the
    /// test started, so operations group under the right test.
    /// </summary>
    public static string Render(
        IReadOnlyList<McpActivityEntry> entries,
        IReadOnlyList<TestBoundary> boundaries,
        int totalTests,
        DateTimeOffset generatedAt,
        string? jsonLogPath = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(boundaries);

        int okCount = entries.Count(e => e.Success);
        int failCount = entries.Count - okCount;

        var sb = new StringBuilder(16 * 1024);
        sb.Append("""
            <!DOCTYPE html><html lang="en"><head><meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>MCP Activity Report</title><style>
            :root{--bg:#0f1419;--surface:#171e26;--surface2:#1e2730;--border:#2b3742;
            --text:#e6edf3;--muted:#9aa7b2;--accent:#e2001a;--accent2:#ff5a6e;
            --ok:#3fb950;--err:#f85149;--mono:"Cascadia Code",Consolas,monospace}
            *{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--text);
            font:14px/1.55 -apple-system,Segoe UI,Roboto,Arial,sans-serif}
            .wrap{max-width:1100px;margin:0 auto;padding:28px 24px 80px}
            h1{font-size:24px;margin:0 0 4px;border-left:4px solid var(--accent);padding-left:12px}
            .meta{color:var(--muted);font-size:13px;margin:0 0 18px;padding-left:16px}
            .meta code{font:12px var(--mono);color:#cfe3ff}
            .stats{display:flex;gap:12px;flex-wrap:wrap;margin:0 0 22px}
            .stat{background:var(--surface);border:1px solid var(--border);border-radius:9px;
            padding:10px 16px;min-width:96px;text-align:center}
            .stat .n{font-size:20px;font-weight:800}.stat .l{color:var(--muted);font-size:11px;
            text-transform:uppercase;letter-spacing:.05em}.stat.ok .n{color:var(--ok)}
            .stat.err .n{color:var(--err)}.stat.acc .n{color:var(--accent2)}
            section{margin:0 0 18px}
            h2{font-size:15px;margin:22px 0 8px;color:var(--accent2);
            background:var(--surface2);border:1px solid var(--border);border-radius:8px;
            padding:9px 14px}h2 .idx{color:var(--muted);font-weight:600}
            h2 .desc{display:block;color:var(--muted);font-weight:400;font-size:12.5px;margin-top:3px}
            table{width:100%;border-collapse:collapse;font-size:13px}
            th,td{text-align:left;padding:6px 10px;border-bottom:1px solid var(--border);vertical-align:top}
            th{color:var(--muted);font-size:11px;text-transform:uppercase;letter-spacing:.04em}
            td.num,td.ms{text-align:right;font:12px var(--mono);color:var(--muted)}
            td.tool{font:12.5px var(--mono);color:#cfe3ff}
            tr.fail td.tool{color:var(--accent2)}
            .st-ok{color:var(--ok);font-weight:700}.st-err{color:var(--err);font-weight:700}
            details{margin-top:4px}summary{cursor:pointer;color:var(--muted);font-size:12px}
            pre{margin:6px 0 0;background:var(--surface2);border:1px solid var(--border);
            border-radius:7px;padding:9px 11px;font:12px/1.5 var(--mono);color:#cfe3ff;
            white-space:pre-wrap;word-break:break-word;max-height:340px;overflow:auto}
            .empty{color:var(--muted);font-style:italic;padding:8px 0}
            footer{color:var(--muted);font-size:12px;margin-top:40px;border-top:1px solid var(--border);padding-top:14px}
            </style></head><body><div class="wrap">
            """);

        sb.Append("<h1>MCP Activity Report</h1>");
        sb.Append("<p class=\"meta\">Generated: ")
          .Append(Esc(generatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
        if (!string.IsNullOrEmpty(jsonLogPath))
            sb.Append("  ·  JSON log: <code>").Append(Esc(jsonLogPath)).Append("</code>");
        sb.Append("</p>");

        sb.Append("<div class=\"stats\">")
          .Append(Stat("acc", entries.Count, "Operations"))
          .Append(Stat("ok", okCount, "OK"))
          .Append(Stat(failCount > 0 ? "err" : "", failCount, "Errors"))
          .Append(Stat("acc", boundaries.Count, totalTests > 0 ? $"Tests / {totalTests}" : "Tests"))
          .Append("</div>");

        if (entries.Count == 0 && boundaries.Count == 0)
        {
            sb.Append("<p class=\"empty\">No activity in this run yet.</p>");
        }
        else
        {
            // Walk the same prefix-stable unified timeline as the grid: a test
            // section opens at each boundary; operations fall under the test
            // whose AfterLogCount precedes them.
            int bi = 0;
            var ops = new List<(McpActivityEntry Entry, int Number)>();
            TestBoundary? section = null;
            bool sectionOpen = false;

            for (int k = 0; k <= entries.Count; k++)
            {
                while (bi < boundaries.Count && boundaries[bi].AfterLogCount <= k)
                {
                    if (sectionOpen) FlushSection(sb, section, ops);
                    section = boundaries[bi];
                    ops.Clear();
                    sectionOpen = true;
                    bi++;
                }
                if (k < entries.Count)
                {
                    if (!sectionOpen) sectionOpen = true; // implicit "pre-test" section
                    ops.Add((entries[k], k + 1));
                }
            }
            if (sectionOpen) FlushSection(sb, section, ops);
        }

        sb.Append("<footer>ptc-xae-mcp · MCP Activity · report generated locally</footer>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private static void FlushSection(
        StringBuilder sb, TestBoundary? boundary,
        List<(McpActivityEntry Entry, int Number)> ops)
    {
        sb.Append("<section>");
        if (boundary is not null)
        {
            sb.Append("<h2><span class=\"idx\">▶ ").Append(boundary.Index).Append(".</span> ")
              .Append(Esc(boundary.Name));
            if (!string.IsNullOrWhiteSpace(boundary.Description))
                sb.Append("<span class=\"desc\">").Append(Esc(boundary.Description)).Append("</span>");
            sb.Append("</h2>");
        }
        else
        {
            sb.Append("<h2>Before the first test</h2>");
        }

        if (ops.Count == 0)
        {
            sb.Append("<p class=\"empty\">No XAE operations.</p></section>");
            return;
        }

        sb.Append("<table><thead><tr><th>#</th><th>Time</th><th>Tool</th>"
                + "<th>Duration&nbsp;(ms)</th><th>Status</th></tr></thead><tbody>");
        foreach (var (e, n) in ops)
        {
            sb.Append("<tr").Append(e.Success ? ">" : " class=\"fail\">");
            sb.Append("<td class=\"num\">").Append(n).Append("</td>");
            sb.Append("<td>").Append(Esc(e.TimestampUtc.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))).Append("</td>");
            sb.Append("<td class=\"tool\">").Append(Esc(e.ToolName)).Append("</td>");
            sb.Append("<td class=\"ms\">").Append((int)e.Duration.TotalMilliseconds).Append("</td>");
            sb.Append("<td>").Append(e.Success ? "<span class=\"st-ok\">✓</span>"
                                               : "<span class=\"st-err\">✗</span>").Append("</td>");
            sb.Append("</tr>");

            if (!string.IsNullOrEmpty(e.Arguments) || !string.IsNullOrEmpty(e.Response))
            {
                sb.Append("<tr").Append(e.Success ? ">" : " class=\"fail\">")
                  .Append("<td></td><td colspan=\"4\"><details><summary>Arguments / Response</summary>");
                if (!string.IsNullOrEmpty(e.Arguments))
                    sb.Append("<pre>").Append(Esc(e.Arguments)).Append("</pre>");
                if (!string.IsNullOrEmpty(e.Response))
                    sb.Append("<pre>").Append(Esc(e.Response)).Append("</pre>");
                sb.Append("</details></td></tr>");
            }
        }
        sb.Append("</tbody></table></section>");
    }

    private static string Stat(string cls, int n, string label) =>
        $"<div class=\"stat {cls}\"><div class=\"n\">{n}</div><div class=\"l\">{Esc(label)}</div></div>";

    // Escape only the markup-significant characters; leave UTF-8 symbols (▶, →,
    // ✓, ✗) intact — the file is written as UTF-8 with a matching charset meta
    // tag. '&' must be replaced first.
    private static string Esc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;");
    }
}
