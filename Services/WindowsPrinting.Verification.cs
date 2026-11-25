using NotepadAvalonia.Models;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace NotepadAvalonia.Services;

internal static partial class WindowsPrinting
{
    internal static bool PrintVerificationPage(string output, string text, PageSetupSettings setup)
    {
        const string printerName = "Microsoft Print to PDF";
        const int invalidPrinterName = 1801;
        const int inputBuffer = 8, outputBuffer = 2;
        if (!OpenPrinterW(printerName, out nint printer, 0))
        {
            int error = Marshal.GetLastWin32Error();
            if (error == invalidPrinterName) return false;
            throw new Win32Exception(error);
        }
        try
        {
            int size = DocumentPropertiesW(0, printer, printerName, 0, 0, 0);
            if (size < 84) throw new IOException("Cannot read the PDF driver's document settings.");
            nint mode = Marshal.AllocHGlobal(size);
            try
            {
                if (DocumentPropertiesW(0, printer, printerName, mode, 0, outputBuffer) != 1)
                    throw new IOException("Cannot read the PDF driver's default settings.");
                ApplyPageSettings(mode, setup);
                if (DocumentPropertiesW(0, printer, printerName, mode, mode, inputBuffer | outputBuffer) != 1)
                    throw new IOException("The PDF driver rejected the verification page settings.");

                nint dc = CreateDCW("WINSPOOL", printerName, null, mode);
                if (dc == 0) throw new Win32Exception();
                try
                {
                    if (!PrintDocument(dc, "NeoEdit print verification", output, text, setup,
                        new PrintFont("Consolas", 11, false, false), DateTime.Now))
                        throw new IOException("The PDF verification job was cancelled.");
                }
                finally { DeleteDC(dc); }
            }
            finally { Marshal.FreeHGlobal(mode); }
            return true;
        }
        finally { ClosePrinter(printer); }
    }

    [DllImport("winspool.drv", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool OpenPrinterW(string name, out nint printer, nint defaults);
    [DllImport("winspool.drv", ExactSpelling = true)]
    private static extern bool ClosePrinter(nint printer);
    [DllImport("winspool.drv", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int DocumentPropertiesW(nint window, nint printer, string name, nint output, nint input, int mode);
}
