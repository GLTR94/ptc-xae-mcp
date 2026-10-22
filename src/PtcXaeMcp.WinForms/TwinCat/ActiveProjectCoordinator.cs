using System.Text.Json;

namespace PtcXaeMcp.WinForms.TwinCat;

internal sealed class ActiveProjectCoordinator(ITwinCatSession session)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string GetActiveProject()
    {
        bool open = session.HasOpenSolution();
        string? path = open ? session.ActiveSolutionPath() : null;
        string? name = path is null ? null : Path.GetFileNameWithoutExtension(path);
        return JsonSerializer.Serialize(new
        {
            solutionOpen = open,
            solutionPath = path,
            solutionName = name,
        }, JsonOpts);
    }
}
