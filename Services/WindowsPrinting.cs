using NotepadAvalonia.Models;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace NotepadAvalonia.Services;

[SupportedOSPlatform("windows")]
internal static partial class WindowsPrinting
{
    private const uint ReturnDc = 0x100;
    private const uint ReturnDefault = 0x400;
    private const uint PrintToFile = 0x20;
    private const uint UseDriverCopies = 0x40000;
    private const uint AllPagesOnly = 0x4 | 0x8 | 0x800000;
    private const uint ResultPrint = 1;
    private const uint StartPageGeneral = uint.MaxValue;

    private const uint SetupUseMargins = 0x2;
    private const uint SetupMetric = 0x8;
    private const int Landscape = 2;

    private const int DpiX = 88;
    private const int DpiY = 90;
    private const int PrintableWidth = 8;
    private const int PrintableHeight = 10;
    private const int PhysicalWidth = 110;
    private const int PhysicalHeight = 111;
    private const int PhysicalOffsetX = 112;
    private const int PhysicalOffsetY = 113;

    private const uint SingleLine = 0x20;
    private const uint NoMnemonic = 0x800;

    public static bool ConfigurePage(nint owner, PageSetupSettings setup)
    {
        var dialog = new PageSetupDialog
        {
            Size = (uint)Marshal.SizeOf<PageSetupDialog>(), Owner = owner, Flags = SetupUseMargins | SetupMetric,
            Margins = new(
                (int)Math.Round(setup.MarginLeft * 100), (int)Math.Round(setup.MarginTop * 100),
                (int)Math.Round(setup.MarginRight * 100), (int)Math.Round(setup.MarginBottom * 100))
        };
        try
        {
            PreparePrinter(owner, setup, ref dialog.DeviceMode, ref dialog.DeviceNames);
            nint previousDpi = SetThreadDpiAwarenessContext(-2); // System-aware common dialog
            bool accepted;
            try { accepted = PageSetupDlgW(ref dialog); }
            finally { if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi); }
            if (!accepted)
            {
                uint error = CommDlgExtendedError();
                if (error != 0) throw new IOException($"The printer setup dialog failed (0x{error:X}).");
                return false;
            }

            bool landscape = ReadOrientation(dialog.DeviceMode) == Landscape;
            double width = dialog.PaperSize.X / 100.0;
            double height = dialog.PaperSize.Y / 100.0;
            if (landscape) (width, height) = (height, width);

            setup.Printer = new(CopyHandle(dialog.DeviceMode), CopyHandle(dialog.DeviceNames));
            setup.Paper = PaperSize.PrinterDefault;
            setup.Landscape = landscape;
            setup.PrinterPaperWidth = width;
            setup.PrinterPaperHeight = height;
            setup.MarginLeft = dialog.Margins.Left / 100.0;
            setup.MarginRight = dialog.Margins.Right / 100.0;
            setup.MarginTop = dialog.Margins.Top / 100.0;
            setup.MarginBottom = dialog.Margins.Bottom / 100.0;
            return true;
        }
        finally
        {
            FreeHandle(dialog.DeviceMode);
            FreeHandle(dialog.DeviceNames);
        }
    }

    public static async Task<bool> PrintAsync(nint owner, string filename, string text,
        PageSetupSettings setup, EditorSettings font, bool direct)
    {
        var dialog = NewPrintDialog(owner, ReturnDc | UseDriverCopies | AllPagesOnly);
        try
        {
            try { PreparePrinter(owner, setup, ref dialog.DeviceMode, ref dialog.DeviceNames); }
            catch (IOException) when (!direct && setup.Printer == null)
            {
                // The interactive property sheet can list printers even if there is no usable default.
                FreeHandle(dialog.DeviceMode);
                FreeHandle(dialog.DeviceNames);
                dialog.DeviceMode = dialog.DeviceNames = 0;
            }
            if (direct)
                dialog.DeviceContext = OpenPrinterContext(dialog.DeviceNames, dialog.DeviceMode);
            else
            {
                InvokePrintDialog(ref dialog);
                if (dialog.Result != ResultPrint) return false;
            }

            if (dialog.DeviceContext == 0) throw new IOException("The selected printer did not provide a device context.");
            var output = (dialog.Flags & PrintToFile) != 0 ? "FILE:" : null;
            var dc = dialog.DeviceContext;
            var typeface = new PrintFont(font.FontFamily, font.FontSize, font.IsBold, font.IsItalic);
            var moment = DateTime.Now;
            return await Task.Run(() => PrintDocument(dc, filename, output, text, setup, typeface, moment));
        }
        finally
        {
            if (dialog.DeviceContext != 0) DeleteDC(dialog.DeviceContext);
            FreeHandle(dialog.DeviceMode);
            FreeHandle(dialog.DeviceNames);
        }
    }

    private static PrintDialog NewPrintDialog(nint owner, uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<PrintDialog>(), Owner = owner, Flags = flags, Copies = 1, StartPage = StartPageGeneral
    };

    private static void InvokePrintDialog(ref PrintDialog dialog)
    {
        nint previousDpi = SetThreadDpiAwarenessContext(-2);
        int result;
        try { result = PrintDlgExW(ref dialog); }
        finally { if (previousDpi != 0) SetThreadDpiAwarenessContext(previousDpi); }
        if (result < 0) throw new IOException("Cannot open the print dialog or locate the printer.", Marshal.GetExceptionForHR(result));
    }

    private static void PreparePrinter(nint owner, PageSetupSettings setup, ref nint mode, ref nint names)
    {
        if (setup.Printer is { } saved)
        {
            mode = RestoreHandle(saved.Mode);
            names = RestoreHandle(saved.Names);
        }
        else
        {
            var defaults = NewPrintDialog(owner, ReturnDefault);
            try { InvokePrintDialog(ref defaults); }
            finally
            {
                mode = defaults.DeviceMode;
                names = defaults.DeviceNames;
            }
        }

        if (mode == 0 || GlobalSize(mode) < 84)
            throw new IOException("No printer is configured.");
        var memory = GlobalLock(mode);
        if (memory == 0) throw new Win32Exception();
        try { ApplyPageSettings(memory, setup); }
        finally { GlobalUnlock(mode); }
    }

    private static void ApplyPageSettings(nint mode, PageSetupSettings setup)
    {
        // Public DEVMODEW fields. Leave the driver-private data untouched.
        const int fieldsOffset = 72, orientationOffset = 76, paperSizeOffset = 78;
        const int orientationField = 0x1, paperSizeField = 0x2, lengthAndWidthFields = 0xC;
        int fields = Marshal.ReadInt32(mode, fieldsOffset) | orientationField;
        Marshal.WriteInt16(mode, orientationOffset, setup.Landscape ? (short)Landscape : (short)1);
        short paper = setup.Paper switch { PaperSize.Letter => 1, PaperSize.Legal => 5, PaperSize.A4 => 9, _ => 0 };
        if (paper != 0)
        {
            fields = (fields & ~lengthAndWidthFields) | paperSizeField;
            Marshal.WriteInt16(mode, paperSizeOffset, paper);
        }
        Marshal.WriteInt32(mode, fieldsOffset, fields);
    }

    private static short ReadOrientation(nint mode)
    {
        var memory = GlobalLock(mode);
        if (memory == 0) throw new Win32Exception();
        try { return Marshal.ReadInt16(memory, 76); }
        finally { GlobalUnlock(mode); }
    }

    private static nint OpenPrinterContext(nint names, nint mode)
    {
        var namesPointer = GlobalLock(names);
        if (namesPointer == 0) throw new Win32Exception();
        var modePointer = GlobalLock(mode);
        try
        {
            if (modePointer == 0) throw new Win32Exception();
            int deviceOffset = (ushort)Marshal.ReadInt16(namesPointer, 2);
            string? device = Marshal.PtrToStringUni(namesPointer + deviceOffset * 2);
            var dc = CreateDCW("WINSPOOL", device, null, modePointer);
            if (dc == 0) throw new Win32Exception();
            return dc;
        }
        finally
        {
            if (modePointer != 0) GlobalUnlock(mode);
            GlobalUnlock(names);
        }
    }

    private static byte[] CopyHandle(nint handle)
    {
        int size = checked((int)GlobalSize(handle));
        var buffer = new byte[size];
        var memory = GlobalLock(handle);
        if (memory == 0) throw new Win32Exception();
        try { Marshal.Copy(memory, buffer, 0, size); }
        finally { GlobalUnlock(handle); }
        return buffer;
    }

    private static nint RestoreHandle(byte[] blob)
    {
        var handle = GlobalAlloc(2, (nuint)blob.Length);
        if (handle == 0) throw new Win32Exception();
        var memory = GlobalLock(handle);
        if (memory == 0)
        {
            GlobalFree(handle);
            throw new Win32Exception();
        }
        try { Marshal.Copy(blob, 0, memory, blob.Length); }
        finally { GlobalUnlock(handle); }
        return handle;
    }

    private static void FreeHandle(nint handle)
    {
        if (handle != 0) GlobalFree(handle);
    }

    private sealed record PrintFont(string Family, double Points, bool Bold, bool Italic);

    private static bool PrintDocument(nint dc, string filename, string? output, string text,
        PageSetupSettings setup, PrintFont font, DateTime moment)
    {
        int dpiX = GetDeviceCaps(dc, DpiX);
        int dpiY = GetDeviceCaps(dc, DpiY);
        int originX = GetDeviceCaps(dc, PhysicalOffsetX);
        int originY = GetDeviceCaps(dc, PhysicalOffsetY);
        static int Pixels(double mm, int dpi) => (int)Math.Round(mm * dpi / 25.4);
        var body = new PrintRect(
            Math.Max(0, Pixels(setup.MarginLeft, dpiX) - originX),
            Math.Max(0, Pixels(setup.MarginTop, dpiY) - originY),
            Math.Min(GetDeviceCaps(dc, PrintableWidth), GetDeviceCaps(dc, PhysicalWidth) - originX - Pixels(setup.MarginRight, dpiX)),
            Math.Min(GetDeviceCaps(dc, PrintableHeight), GetDeviceCaps(dc, PhysicalHeight) - originY - Pixels(setup.MarginBottom, dpiY)));
        if (dpiX <= 0 || dpiY <= 0 || body.Right <= body.Left || body.Bottom <= body.Top)
            throw new IOException("The margins leave no printable area on the selected paper.");

        nint printFont = CreateFontW(-(int)Math.Round(font.Points * dpiY / 72), 0, 0, 0, font.Bold ? 700 : 400,
            font.Italic ? 1u : 0, 0, 0, 1, 0, 0, 0, 0, font.Family);
        if (printFont == 0) throw new Win32Exception();
        nint oldFont = SelectObject(dc, printFont);
        try
        {
            if (oldFont == 0 || oldFont == -1) throw new Win32Exception();
            if (!GetTextMetricsW(dc, out var metrics)) throw new Win32Exception();
            int rowHeight = metrics.Height + metrics.ExternalLeading;
            if (rowHeight <= 0) throw new IOException("Cannot measure the selected print font.");
            int headerRows = PrintService.HasHeader(setup) ? 2 : 0;
            int footerRows = PrintService.HasFooter(setup) ? 2 : 0;
            int rows = (body.Bottom - body.Top) / rowHeight;
            if (rows <= headerRows + footerRows)
                throw new IOException("The font and margins leave no room for document text.");

            int Fit(string line)
            {
                if (!GetTextExtentExPointW(dc, line, line.Length, body.Right - body.Left, out int count, 0, out _))
                    throw new Win32Exception();
                return count;
            }

            var info = new DocumentInfo { Size = Marshal.SizeOf<DocumentInfo>(), Name = filename, Output = output };
            if (StartDocW(dc, in info) <= 0)
            {
                var error = Marshal.GetLastWin32Error();
                // A driver-owned output dialog (PDF, XPS or print to file) can be cancelled.
                if (error is 1223 or 63) return false;
                throw new Win32Exception(error);
            }
            bool completed = false;
            try
            {
                foreach (var page in PrintService.BuildPages(text, filename, setup, rows, Fit, moment))
                {
                    if (StartPage(dc) <= 0) throw new Win32Exception();
                    SelectObject(dc, printFont);
                    SetBkMode(dc, 1);
                    DrawAligned(dc, page.Header, body, body.Top, rowHeight);
                    int y = body.Top + headerRows * rowHeight;
                    foreach (string line in page.Body)
                    {
                        DrawLine(dc, line, body, y, rowHeight, 0);
                        y += rowHeight;
                    }
                    DrawAligned(dc, page.Footer, body, body.Bottom - rowHeight, rowHeight);
                    if (EndPage(dc) <= 0) throw new Win32Exception();
                }
                if (EndDoc(dc) <= 0) throw new Win32Exception();
                completed = true;
                return true;
            }
            finally
            {
                if (!completed) AbortDoc(dc);
            }
        }
        finally
        {
            if (oldFont != 0 && oldFont != -1) SelectObject(dc, oldFont);
            DeleteObject(printFont);
        }
    }

    private static void DrawAligned(nint dc, PrintService.AlignedText text, PrintRect body, int y, int height)
    {
        DrawLine(dc, text.Left, body, y, height, 0);
        DrawLine(dc, text.Center, body, y, height, 1);
        DrawLine(dc, text.Right, body, y, height, 2);
    }

    private static void DrawLine(nint dc, string text, PrintRect body, int y, int height, uint alignment)
    {
        if (text.Length == 0) return;
        var row = new PrintRect(body.Left, y, body.Right, y + height);
        if (DrawTextW(dc, text, text.Length, ref row, alignment | SingleLine | NoMnemonic) == 0)
            throw new Win32Exception();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrintDialog
    {
        public uint Size;
        public nint Owner, DeviceMode, DeviceNames, DeviceContext;
        public uint Flags, ExtendedFlags, Exclusions, RangeCount, MaxRanges;
        public nint PageRanges;
        public uint MinPage, MaxPage, Copies;
        public nint Instance, TemplateName, Callback;
        public uint PropertyPageCount;
        public nint PropertyPages;
        public uint StartPage, Result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PageSetupDialog
    {
        public uint Size;
        public nint Owner, DeviceMode, DeviceNames;
        public uint Flags;
        public PrintSize PaperSize;
        public PrintRect MinimumMargins, Margins;
        public nint Instance, CustomData, SetupHook, PaintHook, TemplateName, Template;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrintRect(int left, int top, int right, int bottom)
    {
        public int Left = left, Top = top, Right = right, Bottom = bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrintSize { public int X, Y; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DocumentInfo
    {
        public int Size;
        public string Name;
        public string? Output, DataType;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TextMetrics
    {
        public int Height, Ascent, Descent, InternalLeading, ExternalLeading, AverageWidth, MaxWidth;
        public int Weight, Overhang, AspectX, AspectY;
        public ushort First, Last, Default, Break;
        public byte Italic, Underlined, StruckOut, Pitch, CharSet;
    }

    [DllImport("comdlg32.dll", ExactSpelling = true)] private static extern int PrintDlgExW(ref PrintDialog dialog);
    [DllImport("comdlg32.dll", ExactSpelling = true)] private static extern bool PageSetupDlgW(ref PageSetupDialog dialog);
    [DllImport("comdlg32.dll", ExactSpelling = true)] private static extern uint CommDlgExtendedError();
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] private static extern nint GlobalAlloc(uint flags, nuint size);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] private static extern nint GlobalLock(nint handle);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern bool GlobalUnlock(nint handle);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern nuint GlobalSize(nint handle);
    [DllImport("kernel32.dll", ExactSpelling = true)] private static extern nint GlobalFree(nint handle);
    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateDCW(string driver, string? device, string? port, nint mode);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern int GetDeviceCaps(nint dc, int index);
    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateFontW(int height, int width, int escapement, int orientation, int weight,
        uint italic, uint underline, uint strikeout, uint charSet, uint precision, uint clip, uint quality, uint pitch, string face);
    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern int SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)] private static extern bool GetTextMetricsW(nint dc, out TextMetrics metrics);
    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetTextExtentExPointW(nint dc, string text, int length, int maxWidth, out int fit, nint widths, out PrintSize size);
    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)] private static extern int StartDocW(nint dc, in DocumentInfo document);
    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)] private static extern int StartPage(nint dc);
    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)] private static extern int EndPage(nint dc);
    [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)] private static extern int EndDoc(nint dc);
    [DllImport("gdi32.dll", ExactSpelling = true)] private static extern int AbortDoc(nint dc);
    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int DrawTextW(nint dc, string text, int length, ref PrintRect rect, uint format);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint SetThreadDpiAwarenessContext(nint context);
}
