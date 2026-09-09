using System.Diagnostics;

namespace RightBTRadio;

/// <summary>
/// Residente de bandeja, sin elevar. Escucha los cambios de dispositivos y dispara la
/// tarea programada que hace el trabajo elevado; la ventana de ajustes es otro proceso,
/// WinUI 3, que se lanza bajo demanda y se libera al cerrarse.
/// </summary>
/// <remarks>
/// Sigue el ciclo de vida de <c>NotifyIcon</c> de <c>TrayApplicationContext</c> de
/// RightKeyboard. No hay IPC con la ventana: aquí el residente nunca escribe la
/// configuración, solo la lee, así que no hay dos escritores que coordinar.
/// </remarks>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly DeviceChangeWindow deviceWindow;
    private readonly NativeTrayMenu menu;
    private readonly NotifyIcon notifyIcon;
    private readonly SynchronizationContext uiContext;
    private Process? settingsProcess;

    public TrayApplicationContext()
    {
        uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        deviceWindow = new DeviceChangeWindow();
        deviceWindow.DevicesChanged += RequestApply;
        menu = new NativeTrayMenu(deviceWindow.Handle, ShowSettings, RequestEnableAll, ExitThread);

        notifyIcon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application,
            Text = "RightBTRadio",
            Visible = true
        };
        notifyIcon.DoubleClick += (_, _) => ShowSettings();
        notifyIcon.MouseUp += (_, args) =>
        {
            if (args.Button == MouseButtons.Right)
            {
                menu.Show();
            }
        };

        EnsureTasksRegistered();

        // Resolución inicial: el estado actual manda antes de quedar esperando eventos.
        RequestApply();

        if (!LoadConfiguration().StartMinimized)
        {
            ShowSettings();
        }
    }

    /// <summary>
    /// Cierra el residente desde otro hilo. Lo llama el instalador a través del evento de
    /// cierre, así que la salida tiene que volver al hilo de la interfaz.
    /// </summary>
    public void RequestExit() => uiContext.Post(_ => ExitThread(), null);

    private static Configuration LoadConfiguration()
    {
        try
        {
            return Configuration.Load();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            MessageBox.Show(
                $"No se pudo cargar la configuración.\n\n{error.Message}",
                "RightBTRadio",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return new Configuration();
        }
    }

    /// <summary>
    /// Las tareas se registran una sola vez, con una elevación explícita. A partir de ahí
    /// la bandeja las dispara sin que Windows vuelva a pedir UAC.
    /// </summary>
    private void EnsureTasksRegistered()
    {
        if (ElevatedTasks.AreRegistered)
        {
            return;
        }

        DialogResult answer = MessageBox.Show(
            "RightBTRadio necesita registrar dos tareas programadas para poder habilitar y " +
            "deshabilitar radios Bluetooth.\n\n" +
            "Windows va a pedir permiso de administrador una sola vez. Después de esto la " +
            "aplicación funciona sin volver a pedirlo.\n\n¿Registrarlas ahora?",
            "RightBTRadio",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (answer != DialogResult.Yes)
        {
            return;
        }

        try
        {
            using Process? elevated = Process.Start(new ProcessStartInfo(StartupManager.TrayExecutablePath)
            {
                Arguments = "--register-tasks",
                UseShellExecute = true,
                Verb = "runas"
            });
            elevated?.WaitForExit();
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // El usuario canceló el aviso de UAC, o no hay permiso para elevar.
            Log.Write($"No se pudieron registrar las tareas: {error.Message}");
        }

        if (!ElevatedTasks.AreRegistered)
        {
            notifyIcon.ShowBalloonTip(
                5000,
                "RightBTRadio",
                "Sin las tareas programadas no se puede cambiar el estado de los radios.",
                ToolTipIcon.Warning);
        }
    }

    private void RequestApply() => ElevatedTasks.Run(ElevatedTasks.ApplyTaskName);

    private void RequestEnableAll()
    {
        if (!ElevatedTasks.Run(ElevatedTasks.EnableAllTaskName))
        {
            return;
        }

        notifyIcon.ShowBalloonTip(
            3000,
            "RightBTRadio",
            "Se habilitaron todos los radios. La prioridad vuelve a aplicarse en el próximo cambio de dispositivos.",
            ToolTipIcon.Info);
    }

    private void ShowSettings()
    {
        if (settingsProcess is { HasExited: false })
        {
            return;
        }

        string? executable = Executables.FindSettings();
        if (executable is null)
        {
            MessageBox.Show(
                "No se encontró RightBTRadio.WinUI.exe junto al ejecutable de bandeja.",
                "RightBTRadio",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            settingsProcess = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false });
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            MessageBox.Show(
                $"No se pudo abrir la ventana de ajustes.\n\n{error.Message}",
                "RightBTRadio",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (settingsProcess is null)
        {
            return;
        }

        settingsProcess.EnableRaisingEvents = true;
        settingsProcess.Exited += (_, _) => RequestApply();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            notifyIcon.Visible = false;
            menu.Dispose();
            deviceWindow.Dispose();
            notifyIcon.Dispose();
            settingsProcess?.Dispose();
        }

        base.Dispose(disposing);
    }
}
