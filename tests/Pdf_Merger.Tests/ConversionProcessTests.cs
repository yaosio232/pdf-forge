using System.Diagnostics;
using Xunit;

namespace Pdf_Merger.Tests;

public class ConversionProcessTests
{
    [Fact]
    public void WordCleanupRefusesAnUnrelatedProcessEvenWithMatchingIdentity()
    {
        using var unrelated = Process.Start(SlowProcess())!;
        var marker = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".word.json");
        try
        {
            File.WriteAllText(marker, System.Text.Json.JsonSerializer.Serialize(new WordConversionWorker.WordIdentity(unrelated.Id, unrelated.StartTime.ToUniversalTime().Ticks)));
            WordConversionWorker.StopOwnedWord(marker);
            Assert.False(unrelated.HasExited);
        }
        finally { File.Delete(marker); if (!unrelated.HasExited) unrelated.Kill(true); unrelated.WaitForExit(); }
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HungWorkerIsStoppedWithoutStoppingUnrelatedProcess(bool cancel)
    {
        using var unrelated = Process.Start(SlowProcess())!;
        using var cancellation = new CancellationTokenSource();
        if (cancel) cancellation.CancelAfter(500);
        var start = Stopwatch.StartNew();
        try
        {
            if (cancel) Assert.ThrowsAny<OperationCanceledException>(() => ConversionProcess.Run(SlowProcess(), cancellation.Token, TimeSpan.FromSeconds(5)));
            else Assert.Throws<TimeoutException>(() => ConversionProcess.Run(SlowProcess(), cancellation.Token, TimeSpan.FromMilliseconds(500)));
            Assert.True(start.Elapsed < TimeSpan.FromSeconds(8));
            Assert.False(unrelated.HasExited);
        }
        finally { if (!unrelated.HasExited) unrelated.Kill(true); unrelated.WaitForExit(); }
    }

    private static ProcessStartInfo SlowProcess()
    {
        var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
        info.ArgumentList.Add("/c"); info.ArgumentList.Add("ping -n 30 127.0.0.1 > nul");
        return info;
    }
}
