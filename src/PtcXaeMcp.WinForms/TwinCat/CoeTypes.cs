namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed record CoeSubIndex(
    string SubIndex,
    string Name,
    string DataType,
    int BitSize,
    string? DefaultValue,
    string? DefaultText,
    string Access,
    bool IsSetting);

internal sealed record CoeObject(
    string Index,
    string Name,
    string Access,
    string ObjectType,
    IReadOnlyList<CoeSubIndex> SubIndexes);

internal sealed record CoeDictionary(
    string TreePath,
    string SlaveType,
    int ObjectCount,
    IReadOnlyList<CoeObject> Objects,
    string? FallbackNote = null);

internal sealed record CoeStartupEntry(
    string Index,
    string SubIndex,
    string ValueHex,
    int BitLength,
    string Transition,
    string? Comment,
    bool Complete);

internal sealed record CoeStartupList(
    string TreePath,
    string SlaveType,
    int EntryCount,
    IReadOnlyList<CoeStartupEntry> Entries);
