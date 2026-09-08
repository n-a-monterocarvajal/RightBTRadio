namespace RightBTRadio;

internal static class Program
{
    internal const string InstanceMutexName = @"Local\RightBTRadio.SingleInstance";

    [STAThread]
    private static void Main()
    {
        using Mutex instanceMutex = new(true, InstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();
        using TrayApplicationContext context = new();
        Application.Run(context);
    }
}
