using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

// Issue #157 item 7: PlcWriteSafetyTests already covers SourceTextInput's strict UTF-8
// file-decoding paths (B-9); this fills the remaining gaps around Resolve's own
// inline/file/both/neither branching and FormatNotes, which had no direct coverage.
public class SourceTextInputTests
{
    [Fact]
    public void Resolve_InlineOnly_ReturnsInlineTextNoError()
    {
        var r = SourceTextInput.Resolve("declaration", "VAR x : INT; END_VAR", null);
        Assert.Equal("VAR x : INT; END_VAR", r.Text);
        Assert.Null(r.Error);
        Assert.Null(r.Note);
    }

    [Fact]
    public void Resolve_NeitherInlineNorFile_ReturnsNullTextNoError()
    {
        // Callers treat a null Text as "leave this section unchanged" — this is not
        // itself an error case for Resolve; the coordinator decides what null means.
        var r = SourceTextInput.Resolve("declaration", null, null);
        Assert.Null(r.Text);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Resolve_EmptyInline_IsDistinctFromNull_ReturnsEmptyText()
    {
        // hasInline is "inline is not null", so an explicit empty string is a real
        // (empty) value to write, not "unset".
        var r = SourceTextInput.Resolve("declaration", "", null);
        Assert.Equal(string.Empty, r.Text);
        Assert.Null(r.Error);
    }

    [Fact]
    public void Resolve_BothInlineAndFile_ReturnsAmbiguityError()
    {
        var r = SourceTextInput.Resolve("declaration", "VAR x : INT; END_VAR", @"C:\some\file.st");
        Assert.Null(r.Text);
        Assert.NotNull(r.Error);
        Assert.StartsWith("[Error]", r.Error);
        Assert.Contains("declaration", r.Error);
        Assert.Contains("declarationFile", r.Error);
        Assert.Contains("not both", r.Error);
    }

    [Fact]
    public void Resolve_FileDoesNotExist_ReturnsNotFoundError()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"ptc157-missing-{Guid.NewGuid():N}.st");
        var r = SourceTextInput.Resolve("implementation", null, missing);
        Assert.Null(r.Text);
        Assert.NotNull(r.Error);
        Assert.StartsWith("[Error] implementationFile not found", r.Error);
        Assert.Contains(missing, r.Error);
    }

    [Fact]
    public void Resolve_WhitespaceOnlyFilePath_IsTreatedAsNoFile()
    {
        // hasFile is "!IsNullOrWhiteSpace" — a whitespace-only path falls through to
        // the inline branch rather than attempting to open "   " as a file.
        var r = SourceTextInput.Resolve("declaration", "inline text", "   ");
        Assert.Equal("inline text", r.Text);
        Assert.Null(r.Error);
    }

    [Fact]
    public void FormatNotes_NoNotes_ReturnsEmptyString()
    {
        var a = SourceTextInput.Resolve("declaration", "x", null);
        var b = SourceTextInput.Resolve("implementation", "y", null);
        Assert.Equal(string.Empty, SourceTextInput.FormatNotes(a, b));
    }

    [Fact]
    public void FormatNotes_JoinsNotesFromSectionsThatCameFromFiles()
    {
        string p = Path.Combine(Path.GetTempPath(), $"ptc157-notes-{Guid.NewGuid():N}.st");
        File.WriteAllText(p, "VAR x : INT; END_VAR");
        try
        {
            var fromFile = SourceTextInput.Resolve("declaration", null, p);
            var inline = SourceTextInput.Resolve("implementation", "x := 1;", null);

            string combined = SourceTextInput.FormatNotes(fromFile, inline);

            Assert.StartsWith(" Read ", combined);
            Assert.Contains("declaration from", combined);
            Assert.DoesNotContain("implementation from", combined);
        }
        finally { File.Delete(p); }
    }
}
