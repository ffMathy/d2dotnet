using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace D2Dotnet.Map.Generator.Interop;

/// <summary>Functions whose addresses are handed to the game (d2_client.c).</summary>
internal static unsafe class Callbacks
{
    // fprintf(stderr, "\n] We got a big Error here! [\n") on a text-mode stderr: each '\n' is "\r\n".
    private static readonly byte[] ErrorMessage = "\r\n] We got a big Error here! [\r\n"u8.ToArray();

    /// <summary>Address of the static d2client_struct (<c>D2Client</c> global in C).</summary>
    internal static uint D2ClientStruct;

    /// <summary>Raw stderr, written unbuffered like C <c>stderr</c>.</summary>
    internal static Stream? Stderr;

    /// <summary><c>DWORD D2ClientInterface(VOID) { return D2Client.dwInit; }</c> (default cdecl).</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static uint D2ClientInterface() => *(uint*)(nuint)D2ClientStruct;

    /// <summary>
    /// <c>VOID __stdcall ExceptionHandler(VOID)</c>: passed to Fog.10019; prints to stderr and
    /// terminates with exit code 0.
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static void ExceptionHandler()
    {
        try
        {
            Stderr?.Write(ErrorMessage);
            Stderr?.Flush();
        }
        finally
        {
            Win32.ExitProcess(0);
        }
    }

    public static uint D2ClientInterfaceAddress
    {
        get
        {
            delegate* unmanaged[Cdecl]<uint> p = &D2ClientInterface;
            return (uint)(nuint)p;
        }
    }

    public static uint ExceptionHandlerAddress
    {
        get
        {
            delegate* unmanaged[Stdcall]<void> p = &ExceptionHandler;
            return (uint)(nuint)p;
        }
    }
}
