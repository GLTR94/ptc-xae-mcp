using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

/// <summary>Issue #239: reading the XAE's "Beim Umbenennen von Objekten" refactoring option from the user .opt.</summary>
public class PlcRefactoringOptionTests
{
    private static string Node(string name, string subKeys) =>
        $"<Single Type=\"x\" Method=\"IArchivable\"><Single Name=\"Name\" Type=\"string\">{name}</Single>" +
        $"<Dictionary Type=\"System.Collections.Hashtable\" Name=\"SubKeys\">{subKeys}</Dictionary>" +
        "<Dictionary Type=\"System.Collections.Hashtable\" Name=\"Values\" /></Single>";

    private static string Entry(string key, string value) =>
        $"<Entry><Key><Single Type=\"string\">{key}</Single></Key><Value>{value}</Value></Entry>";

    private static string Root(string subKeys) => Node("&lt;UserRoot&gt;", subKeys);

    [Fact]
    public void OptionKeyWithEmptySubKeys_IsEnabled() =>
        Assert.Equal(RenameRefactoringSetting.Enabled,
            PlcRefactoringOption.Parse(Root(Entry(PlcRefactoringOption.RefactoringOptionKey, Node(PlcRefactoringOption.RefactoringOptionKey, "")))));

    [Fact]
    public void DisabledAutomaticsPath_IsDisabled()
    {
        string rename = Entry("{0bbe53ad-89ae-479a-8d87-804e90b5f3c0}", Node("x", ""));
        string navigator = Entry(PlcRefactoringOption.NavigatorContext, Node("n", rename));
        string disabled = Entry(PlcRefactoringOption.DisabledAutomatics, Node("d", navigator));
        string xml = Root(Entry("{11111111-0000-0000-0000-000000000000}", Node("other", ""))
                        + Entry(PlcRefactoringOption.RefactoringOptionKey, Node(PlcRefactoringOption.RefactoringOptionKey, disabled)));

        Assert.Equal(RenameRefactoringSetting.Disabled, PlcRefactoringOption.Parse(xml));
    }

    [Fact]
    public void OtherOperationDisabled_StaysEnabledForRename()
    {
        string other = Entry("{22222222-0000-0000-0000-000000000000}", Node("x", ""));
        string navigator = Entry(PlcRefactoringOption.NavigatorContext, Node("n", other));
        string disabled = Entry(PlcRefactoringOption.DisabledAutomatics, Node("d", navigator));

        Assert.Equal(RenameRefactoringSetting.Enabled,
            PlcRefactoringOption.Parse(Root(Entry(PlcRefactoringOption.RefactoringOptionKey, Node("r", disabled)))));
    }

    [Fact]
    public void NoOptionKeyAtAll_MeansDefaults_Enabled() =>
        Assert.Equal(RenameRefactoringSetting.Enabled, PlcRefactoringOption.Parse(Root("")));

    [Fact]
    public void BrokenXmlOrMissingFile_IsUnknown()
    {
        Assert.Equal(RenameRefactoringSetting.Unknown, PlcRefactoringOption.Parse("<Single><broken"));
        Assert.Equal(RenameRefactoringSetting.Unknown,
            PlcRefactoringOption.ReadFile(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N") + ".opt")));
    }

    [Fact]
    public void ThisMachinesFile_IsReadableWhenPresent()
    {
        if (!File.Exists(PlcRefactoringOption.DefaultPath)) return; // no TwinCAT PLC user options here (e.g. CI)
        Assert.NotEqual(RenameRefactoringSetting.Unknown, PlcRefactoringOption.ReadFile(PlcRefactoringOption.DefaultPath));
    }
}
