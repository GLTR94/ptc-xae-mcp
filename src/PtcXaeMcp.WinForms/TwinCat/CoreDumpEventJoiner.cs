using System.Xml.Linq;
using Microsoft.Data.Sqlite;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Joins offline TwinCAT EventLogger data: reads active events from
/// <c>LoggedEvents.db</c> (SQLite) and resolves message text from
/// <c>CurrentEventMessages.xml</c>. Pure file I/O — no running runtime required.
/// </summary>
internal static class CoreDumpEventJoiner
{
    /// <summary>
    /// Returns active events with resolved message text, or an error note when
    /// the files are absent or unreadable. Never throws.
    /// </summary>
    public static (IReadOnlyList<CoreDumpEvent> Events, string? ErrorNote)
        JoinEvents(string dbPath, string xmlPath)
    {
        if (!File.Exists(dbPath))
            return ([], $"LoggedEvents.db not found at: {dbPath}");
        if (!File.Exists(xmlPath))
            return ([], $"CurrentEventMessages.xml not found at: {xmlPath}");

        var catalog = LoadCatalog(xmlPath);

        try
        {
            var events = ReadActiveEvents(dbPath, catalog);
            return (events, null);
        }
        catch (Exception ex)
        {
            return ([], $"[Error reading LoggedEvents.db] {ex.Message}");
        }
    }

    // ── Message catalog (CurrentEventMessages.xml) ────────────────────────────

    // Key: (SourceName upper, EventClassGuid upper, EventId)
    private static Dictionary<(string, string, int), (string? Text, string? Help)>
        LoadCatalog(string xmlPath)
    {
        var catalog = new Dictionary<(string, string, int), (string?, string?)>();
        try
        {
            var doc = XDocument.Load(xmlPath);
            foreach (var entry in doc.Descendants("EventEntry"))
            {
                string sourceId = (entry.Attribute("SourceGuid")
                    ?? entry.Attribute("SourceId")
                    ?? entry.Attribute("Id"))?.Value ?? "";
                string sourceName = entry.Attribute("SourceName")?.Value ?? sourceId;

                foreach (var msg in entry.Descendants("EventMessageEntry"))
                {
                    string classGuid = (msg.Attribute("EventClassGuid")
                        ?? msg.Attribute("ClassId"))?.Value ?? "";
                    if (!int.TryParse(msg.Attribute("EventId")?.Value ?? "0", out int evId))
                        evId = 0;

                    string? text = msg.Elements()
                        .FirstOrDefault(e => e.Name.LocalName is "Text" or "Message")
                        ?.Value.Trim();
                    string? help = msg.Elements()
                        .FirstOrDefault(e => e.Name.LocalName is "Help" or "HelpText")
                        ?.Value.Trim();

                    var key = (
                        (string.IsNullOrEmpty(sourceName) ? sourceId : sourceName).ToUpperInvariant(),
                        classGuid.ToUpperInvariant(),
                        evId);
                    catalog.TryAdd(key, (
                        string.IsNullOrEmpty(text) ? null : text,
                        string.IsNullOrEmpty(help) ? null : help));
                }
            }
        }
        catch { /* return partial/empty catalog on parse failure */ }
        return catalog;
    }

    // ── LoggedEvents.db (SQLite) ──────────────────────────────────────────────

    private static IReadOnlyList<CoreDumpEvent> ReadActiveEvents(
        string dbPath,
        Dictionary<(string, string, int), (string? Text, string? Help)> catalog)
    {
        var cs = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
        }.ToString();

        using var conn = new SqliteConnection(cs);
        conn.Open();

        var tables = ListTables(conn);
        var tableName = PickEventTable(tables);
        if (tableName is null)
            return [];

        var cols = ListColumns(conn, tableName);
        string? sourceCol = Pick(cols, "nsourceid", "sourceid", "source", "sourceguid");
        string? classCol = Pick(cols, "eventclassguid", "classguid", "eeventclass", "classid", "eventclass");
        string? evIdCol = Pick(cols, "neventid", "eventid");
        string? activeCol = Pick(cols, "bactive", "active", "isactive");
        string? nameCol = Pick(cols, "sourcename", "sname");

        if (sourceCol is null || classCol is null) return [];

        var selectCols = new List<string> { $"[{sourceCol}]", $"[{classCol}]" };
        int evIdOrdinal = -1, nameOrdinal = -1;
        if (evIdCol is not null) { evIdOrdinal = selectCols.Count; selectCols.Add($"[{evIdCol}]"); }
        if (nameCol is not null) { nameOrdinal = selectCols.Count; selectCols.Add($"[{nameCol}]"); }

        var where = activeCol is not null
            ? $"WHERE [{activeCol}] = 1 OR [{activeCol}] = 'true' OR [{activeCol}] = 'True'"
            : "";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {string.Join(", ", selectCols)} FROM [{tableName}] {where}";

        var events = new List<CoreDumpEvent>();
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            string sourceId = rdr.GetValue(0)?.ToString() ?? "";
            string classId = rdr.GetValue(1)?.ToString() ?? "";
            int evId = evIdOrdinal >= 0 && !rdr.IsDBNull(evIdOrdinal)
                ? (int)(rdr.GetInt64(evIdOrdinal))
                : 0;
            string sourceName = nameOrdinal >= 0 && !rdr.IsDBNull(nameOrdinal)
                ? rdr.GetValue(nameOrdinal)?.ToString() ?? sourceId
                : sourceId;

            var key = (sourceName.ToUpperInvariant(), classId.ToUpperInvariant(), evId);
            catalog.TryGetValue(key, out var msg);

            events.Add(new CoreDumpEvent(
                SourceId: sourceName,
                EventClassId: classId,
                MessageText: msg.Text,
                HelpText: msg.Help));
        }
        return events;
    }

    private static string? PickEventTable(List<string> tables) =>
        tables.FirstOrDefault(t =>
            t.Contains("event", StringComparison.OrdinalIgnoreCase) &&
            t.Contains("log", StringComparison.OrdinalIgnoreCase))
        ?? tables.FirstOrDefault(t => t.Contains("event", StringComparison.OrdinalIgnoreCase))
        ?? tables.FirstOrDefault();

    private static List<string> ListTables(SqliteConnection conn)
    {
        var list = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read()) list.Add(rdr.GetString(0));
        return list;
    }

    private static List<string> ListColumns(SqliteConnection conn, string table)
    {
        var list = new List<string>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info([{table}])";
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read()) list.Add(rdr.GetString(1)); // col 1 = name
        return list;
    }

    private static string? Pick(List<string> columns, params string[] candidates)
    {
        foreach (var c in candidates)
            foreach (var col in columns)
                if (col.Equals(c, StringComparison.OrdinalIgnoreCase))
                    return col;
        return null;
    }
}
