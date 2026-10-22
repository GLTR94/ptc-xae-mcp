using PtcXaeMcp.WinForms.TwinCat;

namespace PtcXaeMcp.Tests.Fakes;

internal sealed partial class FakeTwinCatSession
{
    // ---- ethercat-topology-edit --------------------------------------------

    public List<(string Name, string Kind)> AddedEtherCatDevices { get; } = new();
    public Exception? AddEtherCatDeviceThrows { get; set; }

    public string AddEtherCatDevice(string name, string deviceKind)
    {
        CallLog.Add($"AddEtherCatDevice({name}, {deviceKind})");
        if (AddEtherCatDeviceThrows is not null) throw AddEtherCatDeviceThrows;
        AddedEtherCatDevices.Add((name, deviceKind));
        return $"Created EtherCAT {deviceKind} 'TIID^{name}' and saved the project.";
    }

    public List<(string ParentPath, EtherCatBoxSpec Box, string? BeforeSibling)> AddedEtherCatBoxes { get; } = new();
    public List<string> RemovedEtherCatBoxes { get; } = new();
    public List<(string TreePath, string Port)> SetPreviousPorts { get; } = new();
    public Exception? AddEtherCatBoxThrows { get; set; }
    public Exception? RemoveEtherCatBoxThrows { get; set; }
    public Exception? SetEtherCatPreviousPortThrows { get; set; }

    public string AddEtherCatBox(string parentPath, EtherCatBoxSpec box, string? beforeSibling)
    {
        CallLog.Add($"AddEtherCatBox(parent={parentPath}, vInfo={box.VInfo}, nodes={box.NodeCount}, before={beforeSibling ?? "<null>"})");
        if (AddEtherCatBoxThrows is not null) throw AddEtherCatBoxThrows;
        AddedEtherCatBoxes.Add((parentPath, box, beforeSibling));
        return $"Added {box.NodeCount} EtherCAT node(s) under '{parentPath}' and saved the project.";
    }

    public string RemoveEtherCatBox(string treePath)
    {
        CallLog.Add($"RemoveEtherCatBox({treePath})");
        if (RemoveEtherCatBoxThrows is not null) throw RemoveEtherCatBoxThrows;
        RemovedEtherCatBoxes.Add(treePath);
        return $"Removed EtherCAT node '{treePath}' and saved the project.";
    }

    public string SetEtherCatPreviousPort(string treePath, string port)
    {
        CallLog.Add($"SetEtherCatPreviousPort({treePath}, {port})");
        if (SetEtherCatPreviousPortThrows is not null) throw SetEtherCatPreviousPortThrows;
        SetPreviousPorts.Add((treePath, port));
        return $"Set previous port '{port}' on '{treePath}' and saved the project.";
    }
}
