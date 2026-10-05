using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace Pdf_Merger;

internal static class WordConversionWorker
{
    internal const string Switch = "--pdf-to-word-worker";
    internal sealed record WordIdentity(int Id, long StartTicks);

    internal static void Convert(string source, string temporary, CancellationToken cancellation)
    {
        var marker = temporary + ".word.json";
        var error = temporary + ".error.txt";
        var executable = Environment.ProcessPath ?? throw new IOException("The PDF Forge executable could not be located.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { Switch, source, temporary, marker, error, Logger.LogFilePath }) start.ArgumentList.Add(argument);
        Logger.LogInfo("PDF to Word: starting isolated STA worker (five-minute timeout).");
        try
        {
            try { ConversionProcess.Run(start, cancellation, TimeSpan.FromMinutes(5)); }
            catch (IOException exception) when (File.Exists(error)) { throw new IOException(File.ReadAllText(error), exception); }
        }
        finally
        {
            StopOwnedWord(marker);
            foreach (var path in new[] { marker, error })
                try { File.Delete(path); } catch (Exception exception) { Logger.LogError("Cleaning a Word worker sidecar failed.", exception); }
        }
    }

    internal static int Execute(string[] args)
    {
        if (args.Length != 6) return 2;
        Logger.SetOutputPath(args[5]);
        try
        {
            PdfToWordConverter.ConvertWithWord(args[1], args[2], args[3]);
            Logger.LogInfo("PDF to Word: worker finished.");
            return 0;
        }
        catch (Exception exception)
        {
            Logger.LogError("PDF to Word: worker failed.", exception);
            try { File.WriteAllText(args[4], exception.Message); } catch { }
            return 1;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    internal static Process StartOwnedWord(string marker)
    {
        string? executable = null;
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\WINWORD.EXE");
                var path = (key?.GetValue(null) as string)?.Trim('"');
                if (path is not null && File.Exists(path)) { executable = path; break; }
            }
        if (executable is null) throw new InvalidOperationException("Microsoft Word desktop is required. Install Word before converting PDFs.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        // /w creates a separate Word instance. /a and /m suppress startup add-ins and AutoExec macros.
        foreach (var argument in new[] { "/w", "/q", "/a", "/m" }) start.ArgumentList.Add(argument);
        var process = Process.Start(start) ?? throw new IOException("The Word conversion instance could not be started.");
        try
        {
            // Publish identity before ANY COM call, including accessing the native object model.
            File.WriteAllText(marker, JsonSerializer.Serialize(new WordIdentity(process.Id, process.StartTime.ToUniversalTime().Ticks)));
            return process;
        }
        catch
        {
            if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); }
            process.Dispose(); throw;
        }
    }

    private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr window, EnumWindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object? nativeObject);

    internal static object GetOwnedApplication(Process process)
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(60))
        {
            if (process.HasExited) throw new IOException("The new Word instance closed before conversion started. Check Word activation and first-run prompts.");
            IntPtr documentWindow = IntPtr.Zero;
            EnumWindows((window, _) =>
            {
                GetWindowThreadProcessId(window, out var id);
                if (id != process.Id) return true;
                EnumChildWindows(window, (child, _) =>
                {
                    var name = new StringBuilder(128); GetClassName(child, name, name.Capacity);
                    if (name.ToString() != "_WwG") return true;
                    documentWindow = child; return false;
                }, IntPtr.Zero);
                return documentWindow == IntPtr.Zero;
            }, IntPtr.Zero);
            if (documentWindow != IntPtr.Zero)
            {
                var dispatch = new Guid("00020400-0000-0000-C000-000000000046");
                var result = AccessibleObjectFromWindow(documentWindow, 0xFFFFFFF0, ref dispatch, out var native);
                if (result == 0 && native is not null)
                {
                    try { return ((dynamic)native).Application; }
                    finally { if (Marshal.IsComObject(native)) Marshal.FinalReleaseComObject(native); }
                }
            }
            Thread.Sleep(200);
        }
        throw new TimeoutException("Word did not become ready within one minute. Check Word activation and first-run prompts, then try again.");
    }

    internal static void StopOwnedWord(string marker)
    {
        if (!File.Exists(marker)) { Logger.LogInfo("Word cleanup: no owned process identity was recorded; no external Word process will be terminated."); return; }
        try
        {
            var identity = JsonSerializer.Deserialize<WordIdentity>(File.ReadAllText(marker))!;
            using var process = Process.GetProcessById(identity.Id);
            if (process.StartTime.ToUniversalTime().Ticks != identity.StartTicks ||
                !string.Equals(process.ProcessName, "WINWORD", StringComparison.OrdinalIgnoreCase)) return;
            if (!process.HasExited) { Logger.LogInfo("Word cleanup: stopping the recorded conversion instance."); process.Kill(); process.WaitForExit(5000); }
        }
        catch (ArgumentException) { /* The owned instance has already exited. */ }
        catch (Exception exception) { Logger.LogError("Cleaning the owned Word process failed.", exception); }
    }
}
