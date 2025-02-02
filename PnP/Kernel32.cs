﻿using Windows.Win32;
using Windows.Win32.Foundation;

namespace Nefarius.Utilities.DeviceManagement.PnP;

internal static class Kernel32
{
    public static bool MethodExists(string libraryName, string methodName)
    {
        var libraryPtr = PInvoke.LoadLibrary(libraryName);

        if (libraryPtr.IsInvalid) return false;

        var procPtr = PInvoke.GetProcAddress(libraryPtr, methodName);

        return procPtr != FARPROC.Null;
    }
}