using System.Text;
using Microsoft.Data.Sqlite;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>
/// Unit tests for the CoreDump file-based parsers.
/// All tests are pure (no disk I/O against real dumps, no XAE, no COM).
/// Fixtures are inline byte arrays, XML strings, and in-memory SQLite databases.
/// </summary>
public class CoreDumpParserTests
{
    // ── CoreDumpClassifier ─────────────────────────────────────────────────────

    // CoreDumpKind is internal; use int for InlineData and cast inside the test.
    // 0=AutoException, 1=Manual, 2=CallStackOnly, 3=Unknown
    [Theory]
    [InlineData(@"C:\Runtimes\UmRT_Template\3.1\Boot\Plc\CoreDump\Port_851.001\Plc\Port_851.001.core", 0)]
    [InlineData(@"C:\Runtimes\UmRT_Template\3.1\Boot\Plc\CoreDump\Port_852.003\Plc\Port_852.003.core", 0)]
    [InlineData(@"C:\Runtimes\UmRT_Template\3.1\Boot\Plc\CoreDump\Port_851_CallStackDump_MySave.A1B2C3D4.core", 2)]
    [InlineData(@"C:\repos\MyProject\PLC\MyPlc.3E4A1B2C-D5E6-7F8A-9B0C-1D2E3F4A5B6C.core", 1)]
    [InlineData(@"C:\repos\something\unknown.core", 3)]
    public void Classifier_CorrectKind(string path, int expectedInt)
    {
        var expected = (CoreDumpKind)expectedInt;
        CoreDumpClassifier.ClassifyPath(path).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"C:\CoreDump\Port_851.001\Plc\Port_851.001.core", 851)]
    [InlineData(@"C:\CoreDump\Port_852_CallStackDump_x.DEADBEEF.core", 852)]
    [InlineData(@"C:\proj\SomePlc.AAAABBBB-CCCC-DDDD-EEEE-FFFFFFFFFFFF.core", null)]
    public void Classifier_ExtractsPort(string path, int? expected)
    {
        CoreDumpClassifier.ExtractPort(path).Should().Be(expected);
    }

    // ── CoreDumpStringExtractor ────────────────────────────────────────────────

    [Fact]
    public void StringExtractor_ExtractsPrintableStrings()
    {
        // Minimal binary: two null-separated printable strings
        byte[] data = Encoding.ASCII.GetBytes("Hello World\0ABCDEF\0xyz"); // "xyz" < MinLength=6
        var strings = CoreDumpStringExtractor.ExtractPrintableStrings(data);
        strings.Should().Contain("Hello World");
        strings.Should().Contain("ABCDEF");
        strings.Should().NotContain("xyz"); // too short
    }

    [Fact]
    public void StringExtractor_FindsIsoTimestamp_UpdatesLastTs()
    {
        // Embed an ISO datetime in otherwise binary garbage
        string embedded = "GarbageAAAAA\x01\x02\x03 2024-06-23 10:19:07 MoreStuffHereXXX\x00";
        byte[] data = Encoding.ASCII.GetBytes(embedded);
        var refTime = new DateTime(2024, 6, 23, 10, 42, 30);

        var (lastTs, _) = CoreDumpStringExtractor.ExtractStateInfo(data, refTime);

        lastTs.Should().NotBeNull();
        lastTs!.Value.Year.Should().Be(2024);
        lastTs.Value.Hour.Should().Be(10);
        lastTs.Value.Minute.Should().Be(19);
    }

    [Fact]
    public void StringExtractor_FindsTcLocaleTimestamp()
    {
        // TwinCAT German locale format DD.MM.YYYY HH:MM:SS (spaces around the timestamp as in real log lines)
        byte[] data = Encoding.ASCII.GetBytes("LOG: 23.06.2024 10:19:07 state=RUN");
        var refTime = new DateTime(2024, 6, 23, 10, 42, 30);

        var (lastTs, _) = CoreDumpStringExtractor.ExtractStateInfo(data, refTime);

        lastTs.Should().NotBeNull();
        lastTs!.Value.Should().Be(new DateTime(2024, 6, 23, 10, 19, 7));
    }

    [Fact]
    public void StringExtractor_ComputesHangGap_FromTimestamp()
    {
        // LastTs = 10:19:07, dump written 10:42:30 → gap = 23 min 23 s
        byte[] data = Encoding.ASCII.GetBytes("STATE 2024-06-23 10:19:07 STEP10 DONE");
        var refTime = new DateTime(2024, 6, 23, 10, 42, 30);

        var (lastTs, _) = CoreDumpStringExtractor.ExtractStateInfo(data, refTime);

        lastTs.Should().NotBeNull();
        var gap = refTime - lastTs!.Value;
        gap.TotalMinutes.Should().BeApproximately(23.38, 0.1);
    }

    [Fact]
    public void StringExtractor_NoTimestamp_ReturnsNull()
    {
        byte[] data = Encoding.ASCII.GetBytes("no-timestamps-here-at-all!!!!!!");
        var (lastTs, _) = CoreDumpStringExtractor.ExtractStateInfo(
            data, new DateTime(2024, 6, 23, 10, 42, 30));
        lastTs.Should().BeNull();
    }

    [Fact]
    public void StringExtractor_DeduplicatesStrings()
    {
        byte[] data = Encoding.ASCII.GetBytes(
            "StepLabel\x00StepLabel\x00StepLabel\x00UniqueEntry");
        var (_, strings) = CoreDumpStringExtractor.ExtractStateInfo(
            data, new DateTime(2024, 6, 23, 10, 0, 0));
        strings.Count(s => s == "StepLabel").Should().Be(1);
    }

    // ── CoreDumpInventory (logic tests via synthesized dir tree) ──────────────

    [Fact]
    public void Inventory_HasCompileInfo_FalseWhenFolderAbsent()
    {
        CoreDumpInventory.HasCompileInfo(null).Should().BeFalse();
        CoreDumpInventory.HasCompileInfo("").Should().BeFalse();
        CoreDumpInventory.HasCompileInfo(@"C:\does-not-exist-12345").Should().BeFalse();
    }

    [Fact]
    public void Inventory_HasCompileInfo_TrueWhenFilePresent()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var ciDir = Path.Combine(tmp, "_CompileInfo");
        Directory.CreateDirectory(ciDir);
        File.WriteAllText(Path.Combine(ciDir, "MyPlc.compileinfo"), "<ci/>");
        try
        {
            CoreDumpInventory.HasCompileInfo(tmp).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    [Fact]
    public void Inventory_HasCompileInfo_FalseWhenDirExistsButNoFile()
    {
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var ciDir = Path.Combine(tmp, "_CompileInfo");
        Directory.CreateDirectory(ciDir);
        try
        {
            CoreDumpInventory.HasCompileInfo(tmp).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(tmp, recursive: true);
        }
    }

    // ── CoreDumpEventJoiner ────────────────────────────────────────────────────

    private const string SampleEventXml = """
        <EventMessageCatalog Version="1">
          <EventEntry SourceGuid="{00000000-0000-0000-0000-000000000001}"
                      SourceName="MAIN.fbError">
            <EventMessageEntry EventClassGuid="{AAAAAAAA-0000-0000-0000-000000000001}"
                               EventId="1">
              <Text>Drive fault on axis {0}</Text>
              <Help>Check servo drive state.</Help>
            </EventMessageEntry>
            <EventMessageEntry EventClassGuid="{AAAAAAAA-0000-0000-0000-000000000001}"
                               EventId="2">
              <Text>E-stop triggered</Text>
            </EventMessageEntry>
          </EventEntry>
        </EventMessageCatalog>
        """;

    [Fact]
    public void EventJoiner_ReturnsErrorNote_WhenDbMissing()
    {
        var (events, note) = CoreDumpEventJoiner.JoinEvents(
            @"C:\does-not-exist\LoggedEvents.db",
            @"C:\does-not-exist\CurrentEventMessages.xml");

        events.Should().BeEmpty();
        note.Should().Contain("not found");
    }

    [Fact]
    public void EventJoiner_ReturnsErrorNote_WhenXmlMissing()
    {
        // Create a minimal (empty) SQLite file so the DB exists
        var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var conn = new SqliteConnection($"Data Source={tmp}"))
            {
                conn.Open(); // creates the file
            }

            var (events, note) = CoreDumpEventJoiner.JoinEvents(
                tmp,
                @"C:\does-not-exist\CurrentEventMessages.xml");

            events.Should().BeEmpty();
            note.Should().Contain("not found");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    [Fact]
    public void EventJoiner_ReturnsActiveEvents_WithResolvedMessage()
    {
        var xmlPath = WriteTempFile(SampleEventXml, ".xml");
        var dbPath = CreateEventDb(activeRows: new[]
        {
            // (SourceName, ClassGuid, EventId, bActive)
            ("MAIN.fbError", "{AAAAAAAA-0000-0000-0000-000000000001}", 1, true),
        });

        try
        {
            var (events, note) = CoreDumpEventJoiner.JoinEvents(dbPath, xmlPath);

            note.Should().BeNull();
            events.Should().HaveCount(1);
            events[0].SourceId.Should().Be("MAIN.fbError");
            events[0].MessageText.Should().Be("Drive fault on axis {0}");
        }
        finally
        {
            SqliteConnection.ClearAllPools(); // release WAL handle before delete
            File.Delete(xmlPath);
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void EventJoiner_SkipsInactiveEvents()
    {
        var xmlPath = WriteTempFile(SampleEventXml, ".xml");
        var dbPath = CreateEventDb(activeRows: new[]
        {
            ("MAIN.fbError", "{AAAAAAAA-0000-0000-0000-000000000001}", 1, false),
        });

        try
        {
            var (events, note) = CoreDumpEventJoiner.JoinEvents(dbPath, xmlPath);
            note.Should().BeNull();
            events.Should().BeEmpty();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(xmlPath);
            File.Delete(dbPath);
        }
    }

    [Fact]
    public void EventJoiner_UnresolvedGuid_StillReturnsEvent_WithNullMessage()
    {
        var xmlPath = WriteTempFile(SampleEventXml, ".xml");
        var dbPath = CreateEventDb(activeRows: new[]
        {
            // ClassGuid not in XML → no message text
            ("MAIN.fbError", "{BBBBBBBB-9999-9999-9999-999999999999}", 99, true),
        });

        try
        {
            var (events, note) = CoreDumpEventJoiner.JoinEvents(dbPath, xmlPath);
            note.Should().BeNull();
            events.Should().HaveCount(1);
            events[0].MessageText.Should().BeNull();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(xmlPath);
            File.Delete(dbPath);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string WriteTempFile(string content, string ext)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ext);
        File.WriteAllText(path, content, Encoding.UTF8);
        return path;
    }

    private static string CreateEventDb(
        IEnumerable<(string SourceName, string ClassGuid, int EventId, bool Active)> activeRows)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db");
        using var conn = new SqliteConnection($"Data Source={path}");
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE LoggedEventEntry (
                nSourceId   INTEGER,
                SourceName  TEXT,
                EventClassGuid TEXT,
                nEventId    INTEGER,
                bActive     INTEGER
            );
            """;
        cmd.ExecuteNonQuery();

        foreach (var (name, classGuid, evId, active) in activeRows)
        {
            cmd.CommandText =
                $"INSERT INTO LoggedEventEntry (nSourceId, SourceName, EventClassGuid, nEventId, bActive) " +
                $"VALUES (1, '{name}', '{classGuid}', {evId}, {(active ? 1 : 0)})";
            cmd.ExecuteNonQuery();
        }

        return path;
    }
}
