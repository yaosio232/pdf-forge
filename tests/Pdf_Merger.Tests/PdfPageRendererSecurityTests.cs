using System.Diagnostics;
using Xunit;

namespace Pdf_Merger.Tests;

public sealed class PdfPageRendererSecurityTests
{
    [Fact]
    public void Hung_native_renderer_hits_wall_limit()
    {
        var started = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => PdfPageRenderer.RunConfined(
            Path.Combine(Environment.SystemDirectory, "ping.exe"), new[] { "-n", "30", "127.0.0.1" },
            Path.GetTempPath(), CancellationToken.None, TimeSpan.FromMilliseconds(400)));
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void Cancellation_stops_native_renderer_with_bounded_wait()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var started = Stopwatch.StartNew();
        Assert.ThrowsAny<OperationCanceledException>(() => PdfPageRenderer.RunConfined(
            Path.Combine(Environment.SystemDirectory, "ping.exe"), new[] { "-n", "30", "127.0.0.1" },
            Path.GetTempPath(), cancellation.Token, TimeSpan.FromSeconds(20)));
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(8));
    }

    [Fact]
    public void Memory_limit_is_applied_before_native_command_runs()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"renderer-{Guid.NewGuid():N}.txt");
        try
        {
            var executable = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var arguments = new[] { "-NoProfile", "-Command", $"Set-Content -LiteralPath '{marker}' -Value started" };
            Assert.Equal(0, PdfPageRenderer.RunConfined(executable, arguments,
                Path.GetTempPath(), CancellationToken.None, TimeSpan.FromSeconds(10)));
            Assert.True(File.Exists(marker));
            File.Delete(marker);
            var exitCode = 0;
            var exception = Record.Exception(() => exitCode = PdfPageRenderer.RunConfined(
                executable, arguments,
                Path.GetTempPath(), CancellationToken.None, TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(5), 1024));
            Assert.True(exception is not null || exitCode != 0);
            Assert.False(File.Exists(marker));
        }
        finally { if (File.Exists(marker)) File.Delete(marker); }
    }

    [Fact]
    public void Cpu_limit_stops_busy_renderer_before_wall_limit()
    {
        var executable = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var started = Stopwatch.StartNew();
        var exitCode = PdfPageRenderer.RunConfined(executable,
            new[] { "-NoProfile", "-Command", "while ($true) {}" }, Path.GetTempPath(),
            CancellationToken.None, TimeSpan.FromSeconds(15), TimeSpan.FromMilliseconds(50));
        Assert.NotEqual(0, exitCode);
        Assert.True(started.Elapsed < TimeSpan.FromSeconds(12), $"Elapsed: {started.Elapsed}");
    }

    [Fact]
    public void Cancellation_terminates_renderer_descendant()
    {
        var marker = Path.Combine(Path.GetTempPath(), $"renderer-child-{Guid.NewGuid():N}.txt");
        using var cancellation = new CancellationTokenSource();
        Process? child = null;
        try
        {
            var executable = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            var ping = Path.Combine(Environment.SystemDirectory, "ping.exe");
            var command = $"$p = Start-Process -FilePath '{ping}' -ArgumentList '-t 127.0.0.1' -PassThru -WindowStyle Hidden; Set-Content -LiteralPath '{marker}' -Value $p.Id; Start-Sleep -Seconds 30";
            var render = Task.Run(() => PdfPageRenderer.RunConfined(executable,
                new[] { "-NoProfile", "-Command", command }, Path.GetTempPath(),
                cancellation.Token, TimeSpan.FromSeconds(40)));
            Assert.True(SpinWait.SpinUntil(() => File.Exists(marker), TimeSpan.FromSeconds(10)));
            child = Process.GetProcessById(int.Parse(File.ReadAllText(marker)));
            cancellation.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => render.GetAwaiter().GetResult());
            Assert.True(SpinWait.SpinUntil(() => child.HasExited, TimeSpan.FromSeconds(5)));
        }
        finally
        {
            cancellation.Cancel();
            if (child is { HasExited: false }) { child.Kill(true); child.WaitForExit(5000); }
            child?.Dispose();
            if (File.Exists(marker)) File.Delete(marker);
        }
    }
}
