namespace PtcXaeMcp.Tests.Fakes;

// Issue #152: read-back verification. Tests name a mutating fake member here to make it
// return normally WITHOUT changing the fake's state — the "the call returned but the change
// did not land" case every write tool must turn into an [Error].
internal sealed partial class FakeTwinCatSession
{
    /// <summary>Names (nameof) of mutating members that return without effect.</summary>
    public HashSet<string> NoOpMutations { get; } = new(StringComparer.Ordinal);

    /// <summary>Set once a PLC-tree mutation (delete/rename) ran.</summary>
    public bool PlcTreeMutated { get; private set; }

    /// <summary>Thrown by ProbePlcNode after a PLC-tree mutation ran (an unreadable read-back).</summary>
    public Exception? ProbeThrowsOnceMutated { get; set; }

    /// <summary>Rename adds the new path but leaves the old one resolvable.</summary>
    public bool RenameKeepsOldPath { get; set; }

    /// <summary>AddLibraryReference lists the new reference with this version instead of the requested one.</summary>
    public string? AddLibraryResolvesToVersion { get; set; }

    /// <summary>SaveAsLibrary(install: true) writes the file but does not install it.</summary>
    public bool SaveAsLibraryInstallIsNoOp { get; set; }
}
