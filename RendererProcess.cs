using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Pdf_Merger;

/// <summary>Starts a renderer suspended, confines its entire process tree, then lets it parse input.</summary>
internal static class RendererProcess
{
    private const uint CreateSuspended = 0x00000004;
    private const uint CreateNoWindow = 0x08000000;
    private const uint WaitObject0 = 0;
    private const uint WaitTimeout = 0x102;
    private const uint WaitFailed = 0xFFFFFFFF;
    private const uint JobObjectLimitProcessTime = 0x00000002;
    private const uint JobObjectLimitJobTime = 0x00000004;
    private const uint JobObjectLimitProcessMemory = 0x00000100;
    private const uint JobObjectLimitJobMemory = 0x00000200;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;

    internal static int Run(string executable, IReadOnlyList<string> arguments, string workingDirectory,
        CancellationToken cancellationToken, TimeSpan wallLimit, TimeSpan cpuLimit, long memoryLimitBytes)
    {
        if (wallLimit <= TimeSpan.Zero || cpuLimit <= TimeSpan.Zero || memoryLimitBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(wallLimit));
        cancellationToken.ThrowIfCancellationRequested();
        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                PerProcessUserTimeLimit = cpuLimit.Ticks,
                PerJobUserTimeLimit = cpuLimit.Ticks,
                LimitFlags = JobObjectLimitProcessTime | JobObjectLimitJobTime | JobObjectLimitProcessMemory |
                    JobObjectLimitJobMemory | JobObjectLimitKillOnJobClose
            },
            ProcessMemoryLimit = (UIntPtr)(ulong)memoryLimitBytes,
            JobMemoryLimit = (UIntPtr)(ulong)memoryLimitBytes
        };
        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Creating the renderer job failed.");
        ProcessInformation process = default;
        var started = false;
        var assigned = false;
        try
        {
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Setting renderer limits failed.");

            var startup = new StartupInfo { Size = (uint)Marshal.SizeOf<StartupInfo>() };
            var command = new StringBuilder(Quote(executable));
            foreach (var argument in arguments) command.Append(' ').Append(Quote(argument));
            if (!CreateProcess(executable, command, IntPtr.Zero, IntPtr.Zero, false,
                    CreateSuspended | CreateNoWindow, IntPtr.Zero, workingDirectory, ref startup, out process))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Starting the renderer failed.");
            started = true;
            if (!AssignProcessToJobObject(job, process.Process))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Confining the renderer failed.");
            assigned = true;
            cancellationToken.ThrowIfCancellationRequested();
            if (ResumeThread(process.Thread) == WaitFailed)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Resuming the confined renderer failed.");

            var clock = Stopwatch.StartNew();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (clock.Elapsed >= wallLimit) throw new TimeoutException($"Preview rendering exceeded its {wallLimit.TotalSeconds:g}-second wall-time limit.");
                var wait = WaitForSingleObject(process.Process, 100);
                if (wait == WaitObject0) break;
                if (wait != WaitTimeout) throw new Win32Exception(Marshal.GetLastWin32Error(), "Waiting for the renderer failed.");
            }
            if (!GetExitCodeProcess(process.Process, out var exitCode))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Reading renderer exit code failed.");
            return (int)exitCode;
        }
        catch
        {
            if (started)
            {
                if (assigned) TerminateJobObject(job, 1);
                else TerminateProcess(process.Process, 1); // The primary thread is still suspended.
                if (WaitForSingleObject(process.Process, 5_000) != WaitObject0)
                    throw new IOException("The confined renderer did not stop within five seconds.");
            }
            throw;
        }
        finally
        {
            if (process.Thread != IntPtr.Zero) CloseHandle(process.Thread);
            if (process.Process != IntPtr.Zero) CloseHandle(process.Process);
            CloseHandle(job); // KILL_ON_JOB_CLOSE also stops any surviving descendants.
        }
    }

    private static string Quote(string value)
    {
        var result = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in value)
        {
            if (character == '\\') { backslashes++; continue; }
            if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1).Append('"');
                backslashes = 0;
                continue;
            }
            result.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }
        return result.Append('\\', backslashes * 2).Append('"').ToString();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public uint Size;
        public string? Reserved;
        public string? Desktop;
        public string? Title;
        public uint X, Y, XSize, YSize, XCountChars, YCountChars, FillAttribute, Flags;
        public ushort ShowWindow, Reserved2;
        public IntPtr ReservedBytes, StandardInput, StandardOutput, StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process, Thread;
        public uint ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr securityAttributes, string? name);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(IntPtr job, int informationClass,
        ref JobObjectExtendedLimitInformation information, uint length);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcess(string application, StringBuilder commandLine,
        IntPtr processAttributes, IntPtr threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags, IntPtr environment, string workingDirectory, ref StartupInfo startup,
        out ProcessInformation process);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(IntPtr process, out uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(IntPtr job, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
