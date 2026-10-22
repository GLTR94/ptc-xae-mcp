using System.Runtime.InteropServices;
using System.Text;

namespace PtcXaeMcp.WinForms.TwinCat;

/// <summary>
/// #161: the flat C API of TwinCAT's native type system (<c>TcTypeSystem.dll</c>, x64), bound at
/// runtime — no build-time dependency, so CI without TwinCAT compiles it unchanged.
/// <para>Every signature is transcribed from Beckhoff's own managed binding
/// (<c>Beckhoff.TwinCAT.TypeSystem.Native.Interop.dll</c>, decompiled from TE2000 14.17.22.1:
/// <c>Native.cs</c> — <c>[DllImport("TcTypeSystem_2.8.dll", CallingConvention = Cdecl)]</c>, a
/// handle-based API: <c>TcTypeSystemLib_CreateHandle(clsid)</c>, strings as handles from
/// <c>TcTypeSystemLib_CreateStringHandle</c> (UTF-16 on Windows), byte arrays as array handles read
/// with <c>GetArrayHandleData</c>/<c>UnlockArrayHandleData</c>). Each out handle carries one reference
/// that is released once with <c>TcTypeSystemLib_ReleaseHandle</c> — the interop's
/// <c>SharedHandle</c> + <c>HandleOwner</c> pair nets out to exactly that.</para>
/// <para>Bound through <see cref="NativeLibrary.Load(string)"/> + <see cref="NativeLibrary.GetExport"/>
/// rather than <c>[DllImport]</c>: the host assembly already owns a DllImportResolver
/// (<c>BeckhoffAssemblyResolver</c>), and the interop's own DLL name (<c>TcTypeSystem_2.8.dll</c>)
/// does not exist next to the file TE2000's parser points at.</para>
/// </summary>
internal sealed class TcTypeSystemNative
{
    /// <summary><c>Native.CLSID_TcTypeSystemProvider</c> of the interop.</summary>
    internal static readonly Guid ClsidTypeSystemProvider = new("E809C538-AC61-5E6B-75E4-E42434C04EDA");

    /// <summary><c>TcTarget.TC_TARGET_64</c> — what TE2000's TmcParser passes as <c>(TcTarget)1</c>.</summary>
    internal const uint TargetX64 = 1;
    /// <summary><c>TcSymbolHdrVer.TC_SYMBOLHDR_VER_3</c> — the AdsSymbolUpAndDownloadHeader3 segment.</summary>
    internal const uint SymbolHeaderVersion3 = 3;
    /// <summary><c>TcSymbolInfoContent.TC_SYMBOLINFO_CONTENT_PLC</c>.</summary>
    internal const uint SymbolInfoContentPlc = 4;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateHandleFn(Guid clsid, out IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReleaseHandleFn(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr CreateStringHandleFn(byte[] source, int length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ReleaseStringHandleFn(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int StringHandleByteLengthFn(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate IntPtr GetArrayHandleDataFn(IntPtr handle, out int length);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void UnlockArrayHandleDataFn(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ReleaseArrayHandleFn(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ErrorMessageFn(int error, out IntPtr message);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateTypeSystemFn(IntPtr provider, uint target, IntPtr name, out IntPtr typeSystem);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LoadTypesFromXmlFileFn(IntPtr typeSystem, IntPtr path, out int numTypes);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateModuleClassManagerFn(IntPtr typeSystem, out IntPtr manager);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ImportModuleClassFromTmcFileFn(IntPtr manager, IntPtr path);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetModuleInfosFn(IntPtr manager, out IntPtr infos);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CollectionSizeFn(IntPtr collection, out int size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CollectionItemFn(IntPtr collection, int index, out IntPtr item);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LoadModuleFn(IntPtr manager, IntPtr info, [MarshalAs(UnmanagedType.I1)] bool readOnly, out IntPtr module);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetNameFn(IntPtr info, out IntPtr name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SymbolInfoFn(IntPtr module, uint objectId, IntPtr instanceName, uint version, uint content, out IntPtr data);

    private readonly IntPtr _library;
    private readonly CreateHandleFn _createHandle;
    private readonly ReleaseHandleFn _releaseHandle;
    private readonly CreateStringHandleFn _createString;
    private readonly ReleaseStringHandleFn _releaseString;
    private readonly StringHandleByteLengthFn _stringByteLength;
    private readonly GetArrayHandleDataFn _arrayData;
    private readonly UnlockArrayHandleDataFn _arrayUnlock;
    private readonly ReleaseArrayHandleFn _arrayRelease;
    private readonly ErrorMessageFn _errorMessage;
    private readonly CreateTypeSystemFn _createTypeSystem;
    private readonly LoadTypesFromXmlFileFn _loadTypesFromXmlFile;
    private readonly CreateModuleClassManagerFn _createModuleClassManager;
    private readonly ImportModuleClassFromTmcFileFn _importModuleClassFromTmcFile;
    private readonly GetModuleInfosFn _moduleInfos;
    private readonly CollectionSizeFn _moduleInfosSize;
    private readonly CollectionItemFn _moduleInfosItem;
    private readonly LoadModuleFn _loadModule;
    private readonly GetNameFn _moduleInfoName;
    private readonly SymbolInfoFn _symbolInfo;

    /// <summary>The file this instance was loaded from.</summary>
    public string LibraryPath { get; }

    private TcTypeSystemNative(string path, IntPtr library)
    {
        LibraryPath = path;
        _library = library;
        _createHandle = Bind<CreateHandleFn>("TcTypeSystemLib_CreateHandle");
        _releaseHandle = Bind<ReleaseHandleFn>("TcTypeSystemLib_ReleaseHandle");
        _createString = Bind<CreateStringHandleFn>("TcTypeSystemLib_CreateStringHandle");
        _releaseString = Bind<ReleaseStringHandleFn>("TcTypeSystemLib_ReleaseStringHandle");
        _stringByteLength = Bind<StringHandleByteLengthFn>("TcTypeSystemLib_StringHandleByteLength");
        _arrayData = Bind<GetArrayHandleDataFn>("TcTypeSystemLib_GetArrayHandleData");
        _arrayUnlock = Bind<UnlockArrayHandleDataFn>("TcTypeSystemLib_UnlockArrayHandleData");
        _arrayRelease = Bind<ReleaseArrayHandleFn>("TcTypeSystemLib_ReleaseArrayHandle");
        _errorMessage = Bind<ErrorMessageFn>("TcTypeSystemLib_ErrorMessage");
        _createTypeSystem = Bind<CreateTypeSystemFn>("ITcTypeSystemProvider_CreateTypeSystem");
        _loadTypesFromXmlFile = Bind<LoadTypesFromXmlFileFn>("ITcTypeSystemEx_LoadTypesFromXmlFile");
        _createModuleClassManager = Bind<CreateModuleClassManagerFn>("ITcTypeSystemEx_CreateModuleClassManager");
        _importModuleClassFromTmcFile = Bind<ImportModuleClassFromTmcFileFn>("ITcModuleClassManager_ImportModuleClassFromTmcFile");
        _moduleInfos = Bind<GetModuleInfosFn>("ITcModuleClassManager_get_ModuleInfos");
        _moduleInfosSize = Bind<CollectionSizeFn>("ITcModuleInfoCollection_get_Size");
        _moduleInfosItem = Bind<CollectionItemFn>("ITcModuleInfoCollection_get_Item");
        _loadModule = Bind<LoadModuleFn>("ITcModuleClassManager_LoadModule");
        _moduleInfoName = Bind<GetNameFn>("ITcModuleInfo_get_Name");
        _symbolInfo = Bind<SymbolInfoFn>("ITcModule_SymbolInfo");
    }

    /// <summary>
    /// Loads the DLL and binds every export used here. Throws <see cref="DllNotFoundException"/>,
    /// <see cref="BadImageFormatException"/> or <see cref="EntryPointNotFoundException"/> — the
    /// exporter turns those into a refusal. The library stays loaded for the process lifetime,
    /// exactly like the interop's DllImport.
    /// </summary>
    public static TcTypeSystemNative Load(string path) => new(path, NativeLibrary.Load(path));

    private T Bind<T>(string export) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_library, export));

    /// <summary>One module's symbol-info payload, exactly as <c>ITcModule.SymbolInfo</c> returns it.</summary>
    internal sealed record ModulePayload(string ModuleName, byte[] Payload);

    /// <summary>
    /// TE2000's <c>TcHmiTmcParser.TmcParser.LoadWithCustomTypes(path, customTypes, name)</c>,
    /// transcribed call for call: CreateTypeSystem(TC_TARGET_64, name) → LoadTypesFromXmlFile per
    /// base-type file → CreateModuleClassManager → ImportModuleClassFromTmcFile → per module info:
    /// LoadModule(readOnly: true) → SymbolInfo(0, name, TC_SYMBOLHDR_VER_3, TC_SYMBOLINFO_CONTENT_PLC).
    /// Modules without a payload are skipped, as there. The raw payload IS the file TE2000's
    /// TmcResultExporter writes as <c>*.DATA_UPLOADINFO2.bin</c> — no format is interpreted here.
    /// </summary>
    public IReadOnlyList<ModulePayload> SymbolInfos(string tmcPath, IReadOnlyList<string> baseTypeFiles, string name)
    {
        IntPtr provider = IntPtr.Zero, typeSystem = IntPtr.Zero, manager = IntPtr.Zero, infos = IntPtr.Zero;
        try
        {
            Check(_createHandle(ClsidTypeSystemProvider, out provider), "TcTypeSystemLib_CreateHandle(TcTypeSystemProvider)");
            if (provider == IntPtr.Zero) throw new InvalidOperationException("TcTypeSystemProvider could not be created.");

            WithString(name, n => Check(_createTypeSystem(provider, TargetX64, n, out typeSystem), "CreateTypeSystem"));
            if (typeSystem == IntPtr.Zero) throw new InvalidOperationException("CreateTypeSystem failed.");

            foreach (string file in baseTypeFiles)
                WithString(file, p => Check(_loadTypesFromXmlFile(typeSystem, p, out _), $"LoadTypesFromXmlFile('{file}')"));

            Check(_createModuleClassManager(typeSystem, out manager), "CreateModuleClassManager");
            if (manager == IntPtr.Zero) throw new InvalidOperationException("Create module class manager failed.");

            WithString(tmcPath, p => Check(_importModuleClassFromTmcFile(manager, p), "ImportModuleClassFromTmcFile"));

            Check(_moduleInfos(manager, out infos), "ModuleInfos");
            if (infos == IntPtr.Zero) throw new InvalidOperationException("Module info collection cannot be null.");
            Check(_moduleInfosSize(infos, out int size), "ModuleInfos.Size");
            if (size == 0) throw new InvalidOperationException("Module info collection cannot be empty.");

            var payloads = new List<ModulePayload>();
            for (int i = 0; i < size; i++)
            {
                IntPtr info = IntPtr.Zero, module = IntPtr.Zero;
                try
                {
                    Check(_moduleInfosItem(infos, i, out info), $"ModuleInfos[{i}]");
                    Check(_loadModule(manager, info, true, out module), $"LoadModule[{i}]");
                    if (module == IntPtr.Zero) throw new InvalidOperationException("Load module failed.");

                    string moduleName = ModuleName(info) ?? Path.GetFileNameWithoutExtension(tmcPath);
                    byte[] payload = [];
                    WithString(name, n =>
                    {
                        IntPtr data = IntPtr.Zero;
                        try
                        {
                            Check(_symbolInfo(module, 0u, n, SymbolHeaderVersion3, SymbolInfoContentPlc, out data),
                                $"SymbolInfo('{moduleName}')");
                            payload = ReadBytes(data);
                        }
                        finally
                        {
                            if (data != IntPtr.Zero) _arrayRelease(data);
                        }
                    });
                    if (payload.Length > 0) payloads.Add(new ModulePayload(moduleName, payload));
                }
                finally
                {
                    Release(module);
                    Release(info);
                }
            }
            return payloads;
        }
        finally
        {
            Release(infos);
            Release(manager);
            Release(typeSystem);
            Release(provider);
        }
    }

    // ---- handle helpers ----------------------------------------------------------

    private string? ModuleName(IntPtr info)
    {
        if (info == IntPtr.Zero || _moduleInfoName(info, out IntPtr name) < 0 || name == IntPtr.Zero) return null;
        try { return ReadString(name); }
        finally { _releaseString(name); }
    }

    /// <summary>Interop's NativeException.Throw: the library's own message, else the HRESULT.</summary>
    private void Check(int hr, string call)
    {
        if (hr >= 0) return;
        string? text = null;
        if (_errorMessage(hr, out IntPtr message) == 0 && message != IntPtr.Zero)
        {
            try { text = ReadString(message); }
            finally { _releaseString(message); }
        }
        throw new InvalidOperationException(
            $"{call} failed (HRESULT 0x{hr:X8}){(string.IsNullOrWhiteSpace(text) ? "" : ": " + text.Trim())}");
    }

    /// <summary>Interop's StringWrapper: an empty string is passed as a null handle.</summary>
    private void WithString(string? value, Action<IntPtr> use)
    {
        IntPtr handle = IntPtr.Zero;
        if (!string.IsNullOrEmpty(value))
        {
            byte[] bytes = Encoding.Unicode.GetBytes(value);
            handle = _createString(bytes, bytes.Length);
        }
        try { use(handle); }
        finally
        {
            if (handle != IntPtr.Zero) _releaseString(handle);
        }
    }

    private string? ReadString(IntPtr handle)
    {
        int length = _stringByteLength(handle);
        if (length <= 0) return null;
        byte[] bytes = new byte[length];
        Marshal.Copy(handle, bytes, 0, length);
        return Encoding.Unicode.GetString(bytes);
    }

    private byte[] ReadBytes(IntPtr array)
    {
        if (array == IntPtr.Zero) return [];
        IntPtr data = _arrayData(array, out int length);
        if (data == IntPtr.Zero || length <= 0) return [];
        try
        {
            byte[] bytes = new byte[length];
            Marshal.Copy(data, bytes, 0, length);
            return bytes;
        }
        finally { _arrayUnlock(array); }
    }

    private void Release(IntPtr handle)
    {
        if (handle != IntPtr.Zero) _releaseHandle(handle);
    }
}
