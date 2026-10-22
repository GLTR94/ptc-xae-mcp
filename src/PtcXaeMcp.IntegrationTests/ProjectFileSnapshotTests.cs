namespace PtcXaeMcp.IntegrationTests;

/// <summary>
/// Offline coverage for the fixture-restore helpers of issue #178. No collection / no fixture:
/// these never touch TwinCAT, so they run on a plain <c>dotnet test</c> and never skip.
/// </summary>
public sealed class ProjectFileSnapshotTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ptc-xae-mcp-snapshot-" + Guid.NewGuid().ToString("N"));

    public ProjectFileSnapshotTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "Sys", "Plc"));
        File.WriteAllText(Path.Combine(_dir, "Fx.sln"), "sln");
        File.WriteAllText(Path.Combine(_dir, "Sys", "Sys.tsproj"), "<ts/>");
        File.WriteAllText(Path.Combine(_dir, "Sys", "Plc", "Plc.plcproj"), "<plc>original</plc>");
        File.WriteAllText(Path.Combine(_dir, "Sys", "Plc", "MAIN.TcPOU"), "pou");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string Plcproj => Path.Combine(_dir, "Sys", "Plc", "Plc.plcproj");

    [Trait("Description", "Offline: Snapshot erfasst nur .plcproj/.tsproj unterhalb des .sln-Ordners.")]
    [Fact]
    public void Capture_TakesOnlyProjectFiles()
    {
        var snap = ProjectFileSnapshot.Capture(Path.Combine(_dir, "Fx.sln"));

        Assert.Equal(2, snap.Files.Count);
        Assert.Contains(snap.Files, f => f.EndsWith("Plc.plcproj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(snap.Files, f => f.EndsWith("Sys.tsproj", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(snap.ChangedFiles());
    }

    [Trait("Description", "Offline: RestoreChanged schreibt nur geänderte Dateien byte-genau zurück.")]
    [Fact]
    public void RestoreChanged_WritesBackOnlyChangedFiles()
    {
        var snap = ProjectFileSnapshot.Capture(Path.Combine(_dir, "Fx.sln"));
        File.WriteAllText(Plcproj, "<plc>broken reference</plc>");

        var restored = snap.RestoreChanged();

        Assert.Single(restored);
        Assert.Equal("<plc>original</plc>", File.ReadAllText(Plcproj));
        Assert.Empty(snap.ChangedFiles());
    }

    [Trait("Description", "Offline: eine gelöschte Projektdatei gilt als geändert und wird wiederhergestellt.")]
    [Fact]
    public void RestoreChanged_RecreatesADeletedFile()
    {
        var snap = ProjectFileSnapshot.Capture(Path.Combine(_dir, "Fx.sln"));
        File.Delete(Plcproj);

        Assert.Single(snap.RestoreChanged());
        Assert.True(File.Exists(Plcproj));
    }

    private const string Listing =
        "3 library reference(s) (source: xae).\n" +
        "  Tc2_Standard: Tc2_Standard * (Beckhoff Automation GmbH) [src=xae]  [file: match]\n" +
        "  Tc2_System: Tc2_System 3.4.26.0 (Beckhoff Automation GmbH) [src=xae]\n" +
        "  Tc3_Module: Tc3_Module * (Beckhoff Automation GmbH) [src=xae]";

    [Trait("Description", "Offline: Referenzliste wird in Platzhalter/Name/Version/Hersteller zerlegt.")]
    [Fact]
    public void Listing_ParsesEveryReferenceLine()
    {
        var refs = LibraryReferenceListing.Parse(Listing);

        Assert.Equal(3, refs.Count);
        Assert.Equal(new LibraryReferenceListing.Reference("Tc2_System", "Tc2_System", "3.4.26.0", "Beckhoff Automation GmbH"), refs[1]);
        Assert.True(LibraryReferenceListing.Contains(Listing, "tc3_module"));
        Assert.False(LibraryReferenceListing.Contains(Listing, "Tc2_MC2"));
    }

    [Trait("Description", "Offline: Name nur im Freitext (z.B. Fehlermeldung) zählt nicht als Referenz.")]
    [Fact]
    public void Listing_IgnoresTheNameInProse()
    {
        Assert.False(LibraryReferenceListing.Contains("[Error] Tc2_MC2 could not be added.", "Tc2_MC2"));
    }

    [Trait("Description", "Offline: Difference ist null bei gleichem Stand (Reihenfolge egal) und nennt fehlende/zusätzliche Referenzen.")]
    [Fact]
    public void Listing_DifferenceReportsMissingAndExtra()
    {
        string reordered = string.Join("\n", Listing.Split('\n').Reverse());
        Assert.Null(LibraryReferenceListing.Difference(Listing, reordered));

        string changed = Listing.Replace("Tc2_Standard * (", "Tc2_Standard 3.3.3.0 (", StringComparison.Ordinal)
            + "\n  Tc2_MC2: Tc2_MC2 * (Beckhoff Automation GmbH) [src=xae]";
        string? diff = LibraryReferenceListing.Difference(Listing, changed);

        Assert.NotNull(diff);
        Assert.Contains("missing: [Tc2_Standard: Tc2_Standard * (Beckhoff Automation GmbH)]", diff);
        Assert.Contains("Tc2_MC2: Tc2_MC2 * (Beckhoff Automation GmbH)", diff);
    }
}
