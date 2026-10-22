using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    public List<LibraryReference> LibraryReferences { get; } = new();
    public List<InstalledLibrary> InstalledLibraries { get; } = new();
    public Exception? ListReferencesThrows { get; set; }
    public Exception? ListInstalledThrows { get; set; }

    public IReadOnlyList<LibraryReference> ListLibraryReferences()
    {
        CallLog.Add("ListLibraryReferences()");
        if (ListReferencesThrows is not null) throw ListReferencesThrows;
        return LibraryReferences;
    }

    public IReadOnlyList<InstalledLibrary> ListInstalledLibraries()
    {
        CallLog.Add("ListInstalledLibraries()");
        if (ListInstalledThrows is not null) throw ListInstalledThrows;
        return InstalledLibraries;
    }

    public Exception? AddLibraryReferenceThrows { get; set; }
    public Exception? RemoveLibraryReferenceThrows { get; set; }
    public Exception? SetLibraryVersionThrows { get; set; }

    public IReadOnlyList<LibraryReference> AddLibraryReference(string name, string version, string? company, bool asPlaceholder)
    {
        CallLog.Add($"AddLibraryReference({name},{version},{company},{asPlaceholder})");
        if (AddLibraryReferenceThrows is not null) throw AddLibraryReferenceThrows;
        if (NoOpMutations.Contains(nameof(AddLibraryReference))) return LibraryReferences;
        LibraryReferences.Add(new LibraryReference(
            Name: name, Version: AddLibraryResolvesToVersion ?? version, Publisher: company ?? string.Empty,
            Placeholder: name,
            EffectiveResolution: $"{name}, {version} ({company ?? string.Empty})",
            Source: LibrarySource.Xae,
            FilePinnedVersion: null,
            VersionMatchesFile: null));
        return LibraryReferences;
    }

    public IReadOnlyList<LibraryReference> RemoveLibraryReference(string name)
    {
        CallLog.Add($"RemoveLibraryReference({name})");
        if (RemoveLibraryReferenceThrows is not null) throw RemoveLibraryReferenceThrows;
        if (NoOpMutations.Contains(nameof(RemoveLibraryReference))) return LibraryReferences;
        // Like ITcPlcLibraryManager.RemoveReference: addressed by the placeholder name.
        LibraryReferences.RemoveAll(r => string.Equals(r.Placeholder, name, StringComparison.OrdinalIgnoreCase));
        return LibraryReferences;
    }

    public IReadOnlyList<LibraryReference> SetLibraryVersion(string name, string version, string? company)
    {
        CallLog.Add($"SetLibraryVersion({name},{version},{company})");
        if (SetLibraryVersionThrows is not null) throw SetLibraryVersionThrows;
        int idx = LibraryReferences.FindIndex(r =>
            string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
        if (idx < 0)
            throw new InvalidOperationException($"Reference '{name}' not found.");
        var old = LibraryReferences[idx];
        LibraryReferences[idx] = old with { Version = version, Publisher = company ?? old.Publisher };
        return LibraryReferences;
    }

    public Exception? InstallLibraryThrows { get; set; }
    public IReadOnlyList<InstalledLibrary> InstallLibrary(string libraryPath, bool overwrite)
    {
        CallLog.Add($"InstallLibrary({libraryPath},{overwrite})");
        if (InstallLibraryThrows is not null) throw InstallLibraryThrows;
        if (!NoOpMutations.Contains(nameof(InstallLibrary)))
            InstalledLibraries.Add(new InstalledLibrary(Path.GetFileNameWithoutExtension(libraryPath), "1.0.0.0", "Fake", "Fake"));
        return InstalledLibraries;
    }

    public Exception? SaveAsLibraryThrows { get; set; }
    public string FakeSaveAsLibraryResult { get; set; } = "Saved library.";
    public string SaveAsLibrary(string outputPath, bool install)
    {
        CallLog.Add($"SaveAsLibrary({outputPath},{install})");
        if (SaveAsLibraryThrows is not null) throw SaveAsLibraryThrows;
        if (!NoOpMutations.Contains(nameof(SaveAsLibrary)))
        {
            File.WriteAllText(outputPath, "fake library");
            if (install && !SaveAsLibraryInstallIsNoOp)
                InstalledLibraries.Add(new InstalledLibrary(Path.GetFileNameWithoutExtension(outputPath), "1.0.0.0", "Fake", "Fake"));
        }
        return FakeSaveAsLibraryResult;
    }

    public List<LibraryParameter> LibraryParameters { get; } = new();
    public Exception? SetLibraryParameterThrows { get; set; }

    public IReadOnlyList<LibraryParameter> ListLibraryParameters(string placeholder)
    {
        CallLog.Add($"ListLibraryParameters({placeholder})");
        return LibraryParameters
            .Where(p => string.Equals(p.Placeholder, placeholder, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public IReadOnlyList<LibraryParameter> SetLibraryParameter(
        string placeholder, string listName, string key, string value)
    {
        CallLog.Add($"SetLibraryParameter({placeholder},{listName},{key},{value})");
        if (SetLibraryParameterThrows is not null) throw SetLibraryParameterThrows;
        int idx = LibraryParameters.FindIndex(p =>
            string.Equals(p.Placeholder, placeholder, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.ListName, listName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
            LibraryParameters[idx] = LibraryParameters[idx] with { Value = value };
        else
            LibraryParameters.Add(new LibraryParameter(placeholder, listName, key, value));
        return ListLibraryParameters(placeholder);
    }
}
