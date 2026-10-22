using System.Runtime.InteropServices;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// Scoped COM IMessageFilter that transparently retries CALL_REJECTED / RETRYLATER
/// on the STA thread. Use with <c>using var _ = ComMessageFilter.Register();</c>
/// around a batch of COM reads; Dispose restores the previous filter.
/// </summary>
/// <remarks>
/// CoRegisterMessageFilter is STA-only. On MTA threads (e.g. the MCP HTTP
/// path), RETRYLATER must be handled by <see cref="ComBusyRetry.Run{T}"/>
/// instead. Both layers are in place so every call path is covered.
/// </remarks>
internal sealed class ComMessageFilter : IDisposable
{
    // IOleMessageFilter: {00000016-0000-0000-C000-000000000046}
    [ComImport, Guid("00000016-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IOleMessageFilter
    {
        [PreserveSig] int HandleInComingCall(int dwCallType, IntPtr htaskCaller, int dwTickCount, IntPtr lpInterfaceInfo);
        [PreserveSig] int RetryRejectedCall(IntPtr htaskCallee, int dwTickCount, int dwRejectType);
        [PreserveSig] int MessagePending(IntPtr htaskCallee, int dwTickCount, int dwPendingType);
    }

    [DllImport("ole32.dll")]
    private static extern int CoRegisterMessageFilter(
        [MarshalAs(UnmanagedType.Interface)] IOleMessageFilter? newFilter,
        [MarshalAs(UnmanagedType.Interface)] out IOleMessageFilter? oldFilter);

    private sealed class RetryFilter(int maxRetryMs) : IOleMessageFilter
    {
        // SERVERCALL_ISHANDLED = 0: accept inbound call normally.
        public int HandleInComingCall(int dwCallType, IntPtr htaskCaller, int dwTickCount, IntPtr lpInterfaceInfo) => 0;

        // dwTickCount = ms waiting for this call; dwRejectType: 0=CALL_REJECTED, 2=RETRYLATER.
        // Return 0-99 → retry after N ms; return -1 → cancel (COMException to caller).
        // Issue #149: a modal XAE window (e.g. an orphaned Go-To dialog) rejects every call until a
        // human closes it — retrying the full budget only burns time. Once ModalGrace has passed and
        // a blocking XAE window is visible, cancel: the caller gets the busy COMException and
        // ComCall names the dialog.
        public int RetryRejectedCall(IntPtr htaskCallee, int dwTickCount, int dwRejectType)
        {
            if (dwTickCount >= maxRetryMs) return -1;
            if (dwTickCount >= (int)ComCall.ModalGrace.TotalMilliseconds &&
                XaeModalProbe.FindBlockingDialog() is not null)
                return -1;
            return 250;
        }

        // PENDINGMSG_WAITNOPROCESS = 2: keep waiting, do not dispatch user messages.
        public int MessagePending(IntPtr htaskCallee, int dwTickCount, int dwPendingType) => 2;
    }

    private IOleMessageFilter? _previous;
    private bool _disposed;

    /// <summary>
    /// Register the retry filter on the calling STA thread.
    /// Returns a disposable scope; restores the previous filter on Dispose.
    /// On MTA threads CoRegisterMessageFilter is a no-op (COM silently ignores it);
    /// the caller is expected to fall back to <see cref="ComBusyRetry"/>.
    /// </summary>
    public static ComMessageFilter Register(int maxRetryMs = 10_000)
    {
        var scope = new ComMessageFilter();
        // HRESULT ignored (CA1806): CoRegisterMessageFilter fails only off an STA thread,
        // where it is documented as a no-op — the class doc above already tells the
        // caller to fall back to ComBusyRetry for that case, so there is nothing new to
        // react to here.
        _ = CoRegisterMessageFilter(new RetryFilter(maxRetryMs), out scope._previous);
        return scope;
    }

    private ComMessageFilter() { }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _ = CoRegisterMessageFilter(_previous, out _);
    }
}
