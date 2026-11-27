using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Notepad.WindowsAutomation;

internal static class Native
{
    internal const int Unavailable = unchecked((int)0x80040201);

    internal static unsafe nint Pointer<T>(T instance) where T : class =>
        (nint)ComInterfaceMarshaller<T>.ConvertToUnmanaged(instance);

    internal static void SetEmpty(nint value) => Marshal.StructureToPtr(default(NativeVariant), value, false);
    internal static void SetBoolean(nint value, bool flag) => SetVariant(value, 11, flag ? -1 : 0);
    internal static void SetNumber(nint value, int number) => SetVariant(value, 3, number);
    internal static void SetString(nint value, string text) => SetVariant(value, 8, Marshal.StringToBSTR(text));

    internal static void SetUnsupported(nint value)
    {
        Marshal.ThrowExceptionForHR(UiaGetReservedNotSupportedValue(out var sentinel));
        SetVariant(value, 13, sentinel);
    }

    private static void SetVariant(nint value, ushort type, nint payload) =>
        Marshal.StructureToPtr(new NativeVariant { Type = type, Value = payload }, value, false);

    internal static nint Array(nint[] pointers)
    {
        nint array = 0;
        try
        {
            array = AllocateArray(13, pointers.Length); // VT_UNKNOWN
            for (int index = 0; index < pointers.Length; index++)
                Marshal.ThrowExceptionForHR(SafeArrayPutElement(array, in index, pointers[index]));
            return array;
        }
        catch
        {
            if (array != 0) SafeArrayDestroy(array);
            throw;
        }
        finally
        {
            foreach (nint pointer in pointers)
                if (pointer != 0) Marshal.Release(pointer);
        }
    }

    internal static nint Array(double[] values)
    {
        nint array = AllocateArray(5, values.Length); // VT_R8
        try
        {
            Marshal.ThrowExceptionForHR(SafeArrayAccessData(array, out var data));
            try { Marshal.Copy(values, 0, data, values.Length); }
            finally { SafeArrayUnaccessData(array); }
            return array;
        }
        catch
        {
            SafeArrayDestroy(array);
            throw;
        }
    }

    private static nint AllocateArray(ushort type, int length)
    {
        nint array = SafeArrayCreateVector(type, 0, (uint)length);
        return array != 0 ? array : throw new OutOfMemoryException();
    }

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern nint SafeArrayCreateVector(ushort type, int lowerBound, uint elements);
    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int SafeArrayPutElement(nint array, in int index, nint value);
    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int SafeArrayAccessData(nint array, out nint value);
    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int SafeArrayUnaccessData(nint array);
    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int SafeArrayDestroy(nint array);

    [DllImport("UIAutomationCore.dll", ExactSpelling = true)]
    private static extern int UiaGetReservedNotSupportedValue(out nint value);
    [DllImport("UIAutomationCore.dll", ExactSpelling = true)]
    internal static extern nint UiaReturnRawElementProvider(nint window, nint wParam, nint lParam, nint provider);
    [DllImport("UIAutomationCore.dll", ExactSpelling = true)]
    internal static extern int UiaHostProviderFromHwnd(nint window, out nint provider);
    [DllImport("UIAutomationCore.dll", ExactSpelling = true)]
    internal static extern int UiaRaiseAutomationEvent(nint provider, int eventId);
    [DllImport("UIAutomationCore.dll", ExactSpelling = true)]
    internal static extern int UiaDisconnectProvider(nint provider);
    [DllImport("UIAutomationCore.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UiaClientsAreListening();
}
