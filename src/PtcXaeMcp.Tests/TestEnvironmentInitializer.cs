using System.Runtime.CompilerServices;
using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests;

internal static class TestEnvironmentInitializer
{
    /// <summary>
    /// Unit tests must not depend on the developer machine's XAE options: the rename note reads the PLC refactoring
    /// option from %LOCALAPPDATA% in production (#239). Tests that need a specific value call RefactoringNote directly.
    /// </summary>
    [ModuleInitializer]
    internal static void Initialize() => PlcRefactoringOption.Probe = () => RenameRefactoringSetting.Unknown;
}
