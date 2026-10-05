using System.Diagnostics;

namespace Pdf_Merger;

internal static class ConversionProcess
{
    internal static void Run(ProcessStartInfo start, CancellationToken cancellation, TimeSpan timeout)
    {
        cancellation.ThrowIfCancellationRequested();
        using var process = Process.Start(start) ?? throw new IOException("The conversion process could not be started.");
        using var deadline = new CancellationTokenSource(timeout);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellation, deadline.Token);
        try
        {
            process.WaitForExitAsync(stop.Token).GetAwaiter().GetResult();
            cancellation.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new IOException($"The conversion process failed (exit code {process.ExitCode}). See the conversion log.");
        }
        catch (OperationCanceledException)
        {
            Logger.LogInfo(cancellation.IsCancellationRequested ? "Conversion cancellation requested; stopping its worker." : "Conversion timed out; stopping its worker.");
            if (!cancellation.IsCancellationRequested) throw new TimeoutException("Conversion timed out. The worker was stopped; you can try again.");
            throw;
        }
        finally
        {
            // Only the process started above and its descendants are eligible for termination.
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                if (!process.WaitForExit(5000)) throw new IOException("The conversion worker could not be stopped. See the log.");
            }
        }
    }
}
