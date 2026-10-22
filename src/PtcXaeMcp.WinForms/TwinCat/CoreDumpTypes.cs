namespace PtcXaeMcp.WinForms.TwinCat;

internal enum CoreDumpKind
{
    /// <summary>Written automatically by the runtime on a PLC exception (nested under Port_NNN.XXX\Plc\).</summary>
    AutoException,
    /// <summary>Written by the engineer via Generate Core Dump; in the PLC project folder as &lt;PLC&gt;.&lt;GUID&gt;.core.</summary>
    Manual,
    /// <summary>Created by CreateCallstackCoreDump(sName); call stack only, no variable values.</summary>
    CallStackOnly,
    Unknown,
}

internal sealed record CoreDumpEntry(
    string FilePath,
    CoreDumpKind Kind,
    DateTime WriteTime,
    long SizeBytes,
    int? Port,
    bool HasCompileInfo,
    string? InstanceName);

internal sealed record CoreDumpEvent(
    string SourceId,
    string EventClassId,
    string? MessageText,
    string? HelpText);
