namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// One named process-data point of an IO-Link device, as broken down from the
/// device's IODD and exposed in the master's <c>RxPdo</c>/<c>TxPdo</c> map.
/// </summary>
/// <param name="Name">Human label, e.g. <c>Valve 1 solenoid 00</c>.</param>
/// <param name="Direction">"Q" for outputs (RxPdo, PLC→device) or "I" for inputs (TxPdo, device→PLC).</param>
/// <param name="DataType">IEC type as TwinCAT reports it, e.g. <c>BIT</c>, <c>USINT</c>.</param>
/// <param name="BitLen">Bit length of the entry.</param>
/// <param name="Index">CoE object index as written in the XML, e.g. <c>#x7000</c>.</param>
/// <param name="SubIndex">CoE sub-index.</param>
internal sealed record IoLinkPdoEntry(
    string Name,
    string Direction,
    string DataType,
    int BitLen,
    string Index,
    int SubIndex);

/// <summary>One IO-Link port (1..4) of a master: the assigned device (if any) and its named process data.</summary>
/// <param name="Port">1-based port number.</param>
/// <param name="Configured">True when a device identity is present on the port.</param>
/// <param name="DeviceName">e.g. <c>VABX-A-P-EL-E12-IOS</c> (null when unconfigured).</param>
/// <param name="VendorName">e.g. <c>Festo</c>.</param>
/// <param name="ProductId">e.g. <c>8000870 VABX-A-P-EL-E12-IOS</c>.</param>
/// <param name="DeviceFamily">e.g. <c>VTUX</c>.</param>
/// <param name="IoddFileName">IODD filename referenced by the port, e.g. <c>Festo-VABX-A-P-EL-20230815-IODD1.1.xml</c>.</param>
/// <param name="ProcessData">Named PDO entries belonging to this port.</param>
internal sealed record IoLinkPort(
    int Port,
    bool Configured,
    string? DeviceName,
    string? VendorName,
    string? ProductId,
    string? DeviceFamily,
    string? IoddFileName,
    IReadOnlyList<IoLinkPdoEntry> ProcessData);

/// <summary>
/// Parsed IO-Link configuration of one master terminal (e.g. EL6224), read back
/// from its <c>ProduceXml(false)</c>. The config persists inside the EtherCAT
/// slave XML.
/// </summary>
/// <param name="MasterPath">Caret-separated TIID tree path of the master.</param>
/// <param name="MasterType">Terminal type, e.g. <c>EL6224</c>.</param>
/// <param name="PortCount">Number of IO-Link ports found.</param>
/// <param name="Ports">Per-port configuration in port-number order.</param>
internal sealed record IoLinkConfig(
    string MasterPath,
    string MasterType,
    int PortCount,
    IReadOnlyList<IoLinkPort> Ports);
