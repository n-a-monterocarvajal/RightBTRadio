using System.Runtime.InteropServices;

namespace RightBTRadio;

internal static class Program
{
    internal const string InstanceMutexName = @"Local\RightBTRadio.SingleInstance";

    /// <summary>
    /// El instalador y el desinstalador señalan este evento para que el residente se
    /// cierre solo. Una aplicación de bandeja no tiene ventana que cerrar, así que sin
    /// esto no hay forma limpia de pedirle que termine.
    /// </summary>
    internal const string CloseEventName = @"Local\RightBTRadio.Close";

    private const uint AttachParentProcess = 0xFFFFFFFF;

    [STAThread]
    private static int Main(string[] args)
    {
        return args.Length == 0 ? RunTray() : RunCommand(args);
    }

    private static int RunTray()
    {
        using Mutex instanceMutex = new(true, InstanceMutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            return 0;
        }

        ApplicationConfiguration.Initialize();
        using EventWaitHandle closeEvent = new(false, EventResetMode.AutoReset, CloseEventName);
        using TrayApplicationContext context = new();
        RegisteredWaitHandle closeRegistration = ThreadPool.RegisterWaitForSingleObject(
            closeEvent,
            (_, _) => context.RequestExit(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: true);

        try
        {
            Application.Run(context);
        }
        finally
        {
            closeRegistration.Unregister(null);
        }

        return 0;
    }

    /// <summary>
    /// Modos sin interfaz. <c>--apply</c> y <c>--enable-all</c> los ejecuta Windows
    /// elevados desde las tareas programadas; el resto existe para probar a mano.
    /// </summary>
    private static int RunCommand(string[] args)
    {
        AttachConsole(AttachParentProcess);

        switch (args[0])
        {
            case "--apply":
                Console.WriteLine($"Nodos cambiados: {RadioService.Apply(Configuration.LoadOrDefault(out _))}");
                return 0;

            case "--enable-all":
                Console.WriteLine($"Nodos habilitados: {RadioService.EnableAll()}");
                return 0;

            case "--list":
                foreach (BluetoothRadio radio in BluetoothRadios.Enumerate())
                {
                    Console.WriteLine(
                        $"problema {radio.Problem,-3}  {radio.HardwareId}  {radio.Name}");
                }

                return 0;

            case "--register-tasks":
                ElevatedTasks.Register(StartupManager.TrayExecutablePath);
                Console.WriteLine("Tareas registradas.");
                return 0;

            case "--unregister-tasks":
                ElevatedTasks.Unregister();
                Console.WriteLine("Tareas eliminadas.");
                return 0;

            default:
                Console.WriteLine(
                    "Uso: RightBTRadio.exe [--apply | --enable-all | --list | " +
                    "--register-tasks | --unregister-tasks]");
                return 2;
        }
    }


    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint processId);
}
