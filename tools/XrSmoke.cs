// Standalone smoke test for the plugin's OpenXR load chain under Proton/Wine:
//   1. LoadLibrary openxr_loader.dll from the BepInEx plugins dir
//   2. xrEnumerateInstanceExtensionProperties
//   3. xrCreateInstance + xrGetInstanceProperties
// Expected without WiVRn: clean failure at step 2/3 with a descriptive result.
// Expected with XR_RUNTIME_JSON=wivrn + wivrn-server up: runtime name printed.
using System;
using System.IO;
using System.Runtime.InteropServices;
using VRKoboldKare;

internal static unsafe class XrSmoke
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryExW(string name, IntPtr hFile, uint flags);
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetProcAddress(IntPtr h, string name);

    private static int Main(string[] args)
    {
        string loader = args.Length > 0 ? args[0] : "openxr_loader.dll";
        Console.WriteLine("XR_RUNTIME_JSON=" + Environment.GetEnvironmentVariable("XR_RUNTIME_JSON"));
        IntPtr h = LoadLibraryExW(loader, IntPtr.Zero, 0x8);
        if (h == IntPtr.Zero) { Console.WriteLine("FAIL: LoadLibrary " + loader); return 1; }
        Console.WriteLine("loaded: " + loader);

        uint n;
        int r = XrApi.xrEnumerateInstanceExtensionProperties(null, 0, out n, null);
        Console.WriteLine("xrEnumerateInstanceExtensionProperties -> " + XrResultCode.Name(r) + " count=" + n);
        if (r != 0) { Console.WriteLine("CHAIN RESULT: loader reachable but no runtime found (expected without WiVRn)."); return 0; }

        var exts = new XrExtensionProperties[n];
        for (int i = 0; i < exts.Length; i++) exts[i].type = XrType.EXTENSION_PROPERTIES;
        r = XrApi.xrEnumerateInstanceExtensionProperties(null, n, out n, exts);
        int d3d11 = 0;
        for (int i = 0; i < n; i++)
        {
            string name;
            fixed (byte* p = exts[i].extensionName) name = XrStr.Read(p, 128);
            if (name == "XR_KHR_D3D11_enable") d3d11 = 1;
            if (n <= 100) Console.WriteLine("  ext: " + name);
        }
        Console.WriteLine("d3d11_enable present: " + (d3d11 == 1));

        var ci = new XrInstanceCreateInfo { type = XrType.INSTANCE_CREATE_INFO };
        byte* appName = ci.applicationName;   // C# 11+: locals are implicitly pinned
        XrStr.Copy(appName, "XrSmoke", 128);
        byte* engName = ci.engineName;
        XrStr.Copy(engName, "smoke", 128);
        ci.apiVersion = 1UL << 48;

        // Always request D3D11 even when unlisted — wineopenxr force-supports
        // graphics extensions on request (translated via DXVK).
        string extName = args.Length > 1 ? args[1] : "XR_KHR_D3D11_enable";
        Console.WriteLine("requesting ext: " + extName);
        IntPtr strPtr = Marshal.StringToHGlobalAnsi(extName);
        IntPtr arrPtr = Marshal.AllocHGlobal(IntPtr.Size);   // const char* const* — array of pointers
        Marshal.WriteIntPtr(arrPtr, 0, strPtr);
        ci.enabledExtensionCount = 1;
        ci.enabledExtensionNames = arrPtr;
        ulong inst;
        r = XrApi.xrCreateInstance(ref ci, out inst);
        Marshal.FreeHGlobal(arrPtr);
        Marshal.FreeHGlobal(strPtr);
        Console.WriteLine("xrCreateInstance (with " + extName + ") -> " + XrResultCode.Name(r));
        if (r != 0) return 0;

        var props = new XrInstanceProperties { type = XrType.INSTANCE_PROPERTIES };
        r = XrApi.xrGetInstanceProperties(inst, ref props);
        byte* rn = props.runtimeName;
        string rtName = XrStr.Read(rn, 128);
        Console.WriteLine("RUNTIME: '" + rtName + "' v" + (props.runtimeVersion >> 48) + "." + ((props.runtimeVersion >> 32) & 0xFFFF));
        XrApi.xrDestroyInstance(inst);
        Console.WriteLine("CHAIN RESULT: instance created OK.");
        return 0;
    }
}
