using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Notepad.WindowsAutomation;

[SupportedOSPlatform("windows")]
public static class AutomationProbe
{
    // Exercise the public Windows client API from a separate MTA thread.
    public static string Verify(nint mainWindow, nint editorWindow, string expected)
    {
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 0));
        try
        {
            var clsid = new Guid("FF48DBA4-60EF-4201-AA87-54103EEF594E");
            var iid = new Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE");
            Marshal.ThrowExceptionForHR(CoCreateInstance(in clsid, 0, 1, in iid, out var automation));
            using var client = new Reference(automation);
            using var main = FromWindow(automation, mainWindow);
            using var direct = FromWindow(automation, editorWindow);
            using var pattern = Pattern(direct.Pointer, 10014);
            using var document = GetDocumentRange(pattern.Pointer);
            Require(ReadText(document.Pointer) == expected, "The editor's TextPattern returns the document.");

            nint name = Marshal.StringToBSTR("NeoEditorText");
            using var descendant = new Reference(0);
            try
            {
                const int automationId = 30011;
                var value = new NativeVariant { Type = 8, Value = name };
                Marshal.ThrowExceptionForHR(Method<PropertyCondition>(automation, 23)(automation, automationId, value, out var condition));
                using var filter = new Reference(condition);
                Marshal.ThrowExceptionForHR(Method<FindFirst>(main.Pointer, 5)(main.Pointer, 4, condition, out descendant.Pointer));
            }
            finally { Marshal.FreeBSTR(name); }
            Require(descendant.Pointer != 0, "The main window's automation tree exposes the editor's TextPattern element.");
            using var treePattern = Pattern(descendant.Pointer, 10014);
            using var treeDocument = GetDocumentRange(treePattern.Pointer);
            Require(ReadText(treeDocument.Pointer) == expected, "The editor found under the main window returns the document.");
            NativePoint selectionPoint = VerifyRanges(treePattern.Pointer);
            var clientPoint = selectionPoint;
            Require(ScreenToClient(mainWindow, ref clientPoint), "The text bounds map to the editor window.");
            nint mouseTarget = ChildWindowFromPointEx(mainWindow, clientPoint, 7); // Skip invisible, disabled, transparent
            Require(mouseTarget != 0 && mouseTarget != editorWindow && mouseTarget != GetParent(editorWindow),
                "The editor's accessibility host lets mouse input pass through.");

            using var text2 = Pattern(descendant.Pointer, 10024);
            Marshal.ThrowExceptionForHR(Method<CaretRange>(text2.Pointer, 10)(text2.Pointer, out int active, out var caret));
            using var caretRange = new Reference(caret);
            Require(caret != 0 && ReadText(caret) == "", "TextPattern2 exposes a collapsed caret.");

            if (GetForegroundWindow() == mainWindow)
            {
                Marshal.ThrowExceptionForHR(Method<OutPointer>(automation, 8)(automation, out var focus));
                using var focused = new Reference(focus);
                string focusId = focus == 0 ? "" : StringProperty(focus, 29);
                string focusName = focus == 0 ? "" : StringProperty(focus, 23);
                Require(active != 0 && focusId == "NeoEditorText",
                    $"UI Automation routes focus to the text provider. Focus: '{focusName}' ({focusId}), active={active}.");
                Require(WindowFromPoint(selectionPoint) == mainWindow, "The accessibility host lets mouse input reach the editor window.");
                return "Windows UI Automation verified TextPattern, caret, character/word/line ranges, selection, geometry, and focus. Mouse hit testing reached the editor.";
            }
            return "Windows UI Automation verified TextPattern, caret, character/word/line ranges, selection, geometry, and mouse pass-through. Foreground focus was unavailable.";
        }
        finally { CoUninitialize(); }
    }

    private static Reference FromWindow(nint automation, nint window)
    {
        Marshal.ThrowExceptionForHR(Method<InputPointer>(automation, 6)(automation, window, out var element));
        return new(element);
    }

    private static Reference Pattern(nint element, int patternId)
    {
        Marshal.ThrowExceptionForHR(Method<IntPointer>(element, 16)(element, patternId, out var pattern));
        Require(pattern != 0, $"Windows UI Automation returned editor pattern {patternId}.");
        return new(pattern);
    }

    private static Reference GetDocumentRange(nint pattern)
    {
        Marshal.ThrowExceptionForHR(Method<OutPointer>(pattern, 7)(pattern, out var range));
        return new(range);
    }

    private static NativePoint VerifyRanges(nint pattern)
    {
        using var range = GetDocumentRange(pattern);
        foreach (var (unit, expected) in new[]
        {
            (TextUnit.Character, "f"), (TextUnit.Word, "first "), (TextUnit.Line, "first word\r\n")
        })
        {
            Marshal.ThrowExceptionForHR(Method<IntArgument>(range.Pointer, 6)(range.Pointer, (int)unit));
            Require(ReadText(range.Pointer) == expected, $"TextPattern expands to the correct {unit} range.");
        }

        Marshal.ThrowExceptionForHR(Method<OutPointer>(pattern, 5)(pattern, out var selection));
        using var array = new Reference(selection);
        Marshal.ThrowExceptionForHR(Method<IntPointer>(selection, 4)(selection, 0, out var selected));
        using var selectedRange = new Reference(selected);
        Require(ReadText(selected) == "word", "TextPattern exposes the active selection.");
        Marshal.ThrowExceptionForHR(Method<OutPointer>(selected, 3)(selected, out var clone));
        using var cloneRange = new Reference(clone);
        Marshal.ThrowExceptionForHR(Method<CompareEndpoints>(clone, 5)(clone, 1, selected, 1, out int comparison));
        Require(comparison == 0, "A cloned range shares the same endpoints.");
        Marshal.ThrowExceptionForHR(Method<MoveEndpoint>(clone, 14)(clone, 0, (int)TextUnit.Character, 1, out int moved));
        Require(moved == 1 && ReadText(clone) == "ord", "The UI Automation client can move a range endpoint by character.");

        Marshal.ThrowExceptionForHR(Method<OutPointer>(selected, 10)(selected, out var rectangles));
        try
        {
            Marshal.ThrowExceptionForHR(SafeArrayGetUBound(rectangles, 1, out int upperBound));
            Require(upperBound >= 3, "The selected range has visible screen geometry.");
            Marshal.ThrowExceptionForHR(SafeArrayAccessData(rectangles, out var data));
            try
            {
                double[] first = new double[4];
                Marshal.Copy(data, first, 0, first.Length);
                return new NativePoint((int)(first[0] + first[2] / 2), (int)(first[1] + first[3] / 2));
            }
            finally { SafeArrayUnaccessData(rectangles); }
        }
        finally { SafeArrayDestroy(rectangles); }
    }

    private static string ReadText(nint range)
    {
        Marshal.ThrowExceptionForHR(Method<IntPointer>(range, 12)(range, -1, out var text));
        try { return Marshal.PtrToStringBSTR(text); }
        finally { Marshal.FreeBSTR(text); }
    }

    private static string StringProperty(nint element, int slot)
    {
        Marshal.ThrowExceptionForHR(Method<OutPointer>(element, slot)(element, out var text));
        try { return text == 0 ? "" : Marshal.PtrToStringBSTR(text); }
        finally { Marshal.FreeBSTR(text); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Reference(nint pointer) : IDisposable
    {
        public nint Pointer = pointer;
        public void Dispose()
        {
            if (Pointer != 0) Marshal.Release(Pointer);
        }
    }

    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    private delegate int OutPointer(nint self, out nint output);
    private delegate int InputPointer(nint self, nint input, out nint output);
    private delegate int IntPointer(nint self, int input, out nint output);
    private delegate int PropertyCondition(nint self, int property, NativeVariant value, out nint condition);
    private delegate int FindFirst(nint self, int scope, nint condition, out nint output);
    private delegate int IntArgument(nint self, int input);
    private delegate int CaretRange(nint self, out int active, out nint range);
    private delegate int CompareEndpoints(nint self, int endpoint, nint other, int otherEndpoint, out int result);
    private delegate int MoveEndpoint(nint self, int endpoint, int unit, int count, out int result);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct NativePoint(int X, int Y);

    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int SafeArrayGetUBound(nint array, uint dimension, out int bound);
    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int SafeArrayAccessData(nint array, out nint data);
    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int SafeArrayUnaccessData(nint array);
    [DllImport("oleaut32.dll", ExactSpelling = true)]
    private static extern int SafeArrayDestroy(nint array);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetParent(nint window);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool ScreenToClient(nint window, ref NativePoint point);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint ChildWindowFromPointEx(nint parent, NativePoint point, uint flags);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
}
