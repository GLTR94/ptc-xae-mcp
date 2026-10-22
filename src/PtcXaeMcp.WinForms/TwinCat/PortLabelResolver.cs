using System.Text.Json;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Resolves (modelName, portLetter) → physical connector label (X1, X2 …).
/// Seed data is loaded from port-labels.json in the app directory.
/// Returns null for unknown combinations without throwing.
/// </summary>
internal sealed class PortLabelResolver
{
    // model → (portLetter → label)
    private readonly Dictionary<string, Dictionary<string, string>> _table;

    public PortLabelResolver(string jsonPath)
    {
        _table = Load(jsonPath);
    }

    public string? Resolve(string modelName, string portLetter)
    {
        if (string.IsNullOrEmpty(modelName) || string.IsNullOrEmpty(portLetter))
            return null;

        if (!_table.TryGetValue(modelName, out var ports))
            return null;

        return ports.TryGetValue(portLetter, out var label) ? label : null;
    }

    private static Dictionary<string, Dictionary<string, string>> Load(string jsonPath)
    {
        try
        {
            if (!File.Exists(jsonPath)) return new();
            using var stream = File.OpenRead(jsonPath);
            var outer = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(stream);
            return outer ?? new();
        }
        catch
        {
            return new();
        }
    }

    // Shared instance loaded from the app's output directory.
    private static PortLabelResolver? _instance;
    private static readonly object _lock = new();

    internal static PortLabelResolver Instance
    {
        get
        {
            if (_instance is not null) return _instance;
            lock (_lock)
            {
                if (_instance is not null) return _instance;
                var dir = AppContext.BaseDirectory;
                var path = Path.Combine(dir, "port-labels.json");
                _instance = new PortLabelResolver(path);
                return _instance;
            }
        }
    }
}
