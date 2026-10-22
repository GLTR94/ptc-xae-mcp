namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Issue #152 B-16: wording for a multi-step create whose later step failed after the object was
/// created. The object exists — the text must say where and which step is missing, so the caller
/// neither assumes nothing happened nor retries into "already exists". Without the "[Error] " prefix
/// (the coordinators add it).
/// </summary>
internal static class PartialCreate
{
    public static string Describe(string what, string createdAt, string missingStep, string cause, string remedy) =>
        $"{what} was created at {createdAt}, but its {missingStep} was NOT applied: {cause} " +
        $"The object exists. {remedy}";
}
