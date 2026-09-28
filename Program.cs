namespace Pdf_Merger;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Logger.LogInfo("Application started.");
        Application.Run(new Form1());
        Logger.LogInfo("Application closed.");
    }
}
