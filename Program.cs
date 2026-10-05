namespace Pdf_Merger;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == WordConversionWorker.Switch)
        {
            Environment.ExitCode = WordConversionWorker.Execute(args);
            return;
        }
        ApplicationConfiguration.Initialize();
        Logger.LogInfo("Application started.");
        Application.Run(new Form1());
        Logger.LogInfo("Application closed.");
    }
}
