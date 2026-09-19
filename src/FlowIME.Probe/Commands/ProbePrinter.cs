using FlowIME.Core.Models;

namespace FlowIME.Probe.Commands;

internal static class ProbePrinter
{
    internal static void PrintWindow(WindowContext window)
    {
        Console.WriteLine($"HWND: 0x{window.Hwnd.ToInt64():X}");
        Console.WriteLine($"Process: {Display(window.ProcessName)}");
        Console.WriteLine($"PID: {window.ProcessId}");
        Console.WriteLine($"Thread: {window.ThreadId}");
        Console.WriteLine($"Path: {Display(window.ExecutablePath)}");
        Console.WriteLine($"Title: {Display(window.WindowTitle)}");
        Console.WriteLine($"Class: {Display(window.WindowClass)}");
        Console.WriteLine($"PackageFamily: {Display(window.PackageFamilyName)}");
    }

    private static string Display(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "<unavailable>" : value;
}
