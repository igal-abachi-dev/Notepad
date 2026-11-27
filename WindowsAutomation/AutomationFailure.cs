using System;
using System.Diagnostics;

namespace Notepad.WindowsAutomation;

internal static class AutomationFailure
{
    internal static bool CanRecover(Exception exception) =>
        exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    internal static void Report(string operation, Exception exception)
    {
        try { Trace.TraceWarning("UI Automation {0} failed: {1}", operation, exception.GetType().Name); }
        catch (Exception traceError) when (CanRecover(traceError)) { }
    }
}
