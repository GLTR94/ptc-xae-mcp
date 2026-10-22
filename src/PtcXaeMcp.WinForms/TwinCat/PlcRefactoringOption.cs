using System.Xml.Linq;

namespace PtcXaeMcp.WinForms.TwinCat;

internal enum RenameRefactoringSetting { Unknown, Enabled, Disabled }

/// <summary>
/// Issue #239: a rename through the Automation Interface runs inside an automation scope, so the PLC engineering
/// passes "silent" to the CODESYS refactoring helper — no prompt, no preview. Whether the callers are rewritten then
/// depends only on the option Tools &gt; Options &gt; TwinCAT &gt; PLC Environment &gt; Refactoring, row "Navigator",
/// column "Beim Umbenennen von Objekten". It is persisted (read-only here — the XAE caches and rewrites it) in
/// %LOCALAPPDATA%\Beckhoff\TwinCAT\PlcEngineering\Options\TwinCAT PLC ControlUser.opt and is DISABLED only when the
/// sub-key path {Refactoring}\DISABLED_AUTOMATICS\{Navigator}\{RenameObject} exists.
/// </summary>
internal static class PlcRefactoringOption
{
    internal const string RefactoringOptionKey = "{6C08473A-63C9-4952-92A6-FF0692897DE9}";
    internal const string DisabledAutomatics = "DISABLED_AUTOMATICS";
    internal const string NavigatorContext = "{5146F6D9-6EE8-4406-B2EF-E0096E431E34}";
    internal const string RenameObjectOperation = "{0BBE53AD-89AE-479A-8D87-804E90B5F3C0}";

    internal static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Beckhoff", "TwinCAT", "PlcEngineering", "Options", "TwinCAT PLC ControlUser.opt");

    /// <summary>Test seam; production reads <see cref="DefaultPath"/>.</summary>
    internal static Func<RenameRefactoringSetting> Probe { get; set; } = () => ReadFile(DefaultPath);

    public static RenameRefactoringSetting ReadFile(string path)
    {
        try { return File.Exists(path) ? Parse(File.ReadAllText(path)) : RenameRefactoringSetting.Unknown; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return RenameRefactoringSetting.Unknown; }
    }

    /// <summary>Unknown for unreadable XML; a missing option key means CODESYS defaults (enabled).</summary>
    public static RenameRefactoringSetting Parse(string optXml)
    {
        XDocument doc;
        try { doc = XDocument.Parse(optXml); }
        catch (System.Xml.XmlException) { return RenameRefactoringSetting.Unknown; }

        XElement? option = doc.Descendants("Entry")
            .Where(e => KeyOf(e) == RefactoringOptionKey)
            .Select(ValueNode)
            .FirstOrDefault(v => v is not null);
        if (option is null) return RenameRefactoringSetting.Enabled;

        XElement? rename = Child(Child(Child(option, DisabledAutomatics), NavigatorContext), RenameObjectOperation);
        return rename is null ? RenameRefactoringSetting.Enabled : RenameRefactoringSetting.Disabled;
    }

    private static string? KeyOf(XElement entry) => entry.Element("Key")?.Element("Single")?.Value.Trim().ToUpperInvariant();

    private static XElement? ValueNode(XElement entry) => entry.Element("Value")?.Element("Single");

    private static XElement? Child(XElement? node, string key) =>
        node?.Elements("Dictionary")
            .Where(d => (string?)d.Attribute("Name") == "SubKeys")
            .Elements("Entry")
            .Where(e => KeyOf(e) == key.ToUpperInvariant())
            .Select(ValueNode)
            .FirstOrDefault(v => v is not null);
}
