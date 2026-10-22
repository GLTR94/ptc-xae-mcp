using System.Text;

namespace PtcXaeMcp.WinForms;

/// <summary>
/// The host's run log (environment checks, apply-schema progress). Thread-safe; the GUI
/// mirrors it through <see cref="LineAppended"/> and <c>twincat_get_log</c> reads
/// <see cref="Text"/> — before issue #268 the tool read the GUI TextBox via Invoke.
/// Bounded: past <see cref="MaxChars"/> the oldest part is dropped.
/// </summary>
internal sealed class HostRunLog
{
    internal const int MaxChars = 2_000_000;

    private readonly object _gate = new();
    private readonly StringBuilder _text = new();

    /// <summary>Raised after a line was appended, on the appending thread, inside the log's
    /// lock — so subscribers see lines and clears in the order the log has them. Handlers
    /// must only queue work (the GUI posts to its message loop), never call back into the log.</summary>
    public event Action<string>? LineAppended;

    /// <summary>Raised after <see cref="Clear"/>; same ordering rule as <see cref="LineAppended"/>.</summary>
    public event Action? Cleared;

    public string Text
    {
        get { lock (_gate) return _text.ToString(); }
    }

    /// <summary>The newest <paramref name="maxChars"/> characters behind a note of how much was
    /// omitted (<c>twincat_get_log</c>); copies only that tail. 0 or less returns everything.</summary>
    public string Tail(int maxChars)
    {
        lock (_gate)
        {
            if (maxChars <= 0 || _text.Length <= maxChars) return _text.ToString();
            int cut = _text.Length - maxChars;
            return LogTail.OmittedNote(cut, maxChars) + _text.ToString(cut, maxChars);
        }
    }

    public void Append(string line)
    {
        lock (_gate)
        {
            _text.Append(line).Append(Environment.NewLine);
            if (_text.Length > MaxChars)
                _text.Remove(0, _text.Length - MaxChars / 2);
            LineAppended?.Invoke(line);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _text.Clear();
            Cleared?.Invoke();
        }
    }
}
