namespace PtcXaeMcp.WinForms.TwinCat;

// ci-twincat-compile-seams (issue #47). When the solution is built without
// TwinCAT (hosted CI: -p:TwinCat=false / Beckhoff DLLs absent), the COM-only
// partials of TwinCatComSession (Scope, EventLogger) compile to fallbacks
// instead of the interop code. Those fallbacks report this single, honest
// message so a caller / operator sees *why* the path is unavailable rather
// than a confusing failure. Builds with TwinCAT never reference this.
internal static class TwinCatNotBuilt
{
    public const string Message =
        "This build was produced without TwinCAT (hosted CI / -p:TwinCat=false), " +
        "so the TwinCAT COM path is not compiled in. Run on a machine with " +
        "TwinCAT XAE installed (the live suite) to exercise it.";

    public static PlatformNotSupportedException Exception() => new(Message);

    // hmi-server: the HMI partial has its own seam ($(TcHmi)), because TE2000 is an
    // optional add-on — a TwinCAT box without it still builds everything else.
    public const string HmiMessage =
        "This build was produced without the TwinCAT HMI Automation Interface " +
        "(TE2000 TcHmiAutomation.dll not found at build time, or -p:TcHmi=false), " +
        "so the HMI tools are not compiled in. Install TE2000 and rebuild.";

    public static PlatformNotSupportedException HmiException() => new(HmiMessage);
}
