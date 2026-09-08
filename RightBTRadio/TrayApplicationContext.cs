namespace RightBTRadio;

/// <summary>
/// Residente de bandeja. Sigue el ciclo de vida de <c>NotifyIcon</c> de
/// <c>TrayApplicationContext</c> de RightKeyboard, sin su selector de dispositivo,
/// su IPC ni su frontend en otro proceso.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Configuration configuration;
    private readonly DeviceChangeWindow deviceWindow;
    private readonly NativeTrayMenu menu;
    private readonly NotifyIcon notifyIcon;
    private SettingsForm? settingsForm;

    public TrayApplicationContext()
    {
        configuration = LoadConfiguration();

        deviceWindow = new DeviceChangeWindow();
        deviceWindow.DevicesChanged += Resolve;
        menu = new NativeTrayMenu(deviceWindow.Handle, ShowSettings, EnableAllRadios, ExitThread);

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

        // Resolución inicial: el estado actual manda antes de quedar esperando eventos.
        Resolve();

        if (!configuration.StartMinimized)
        {
            ShowSettings();
        }
    }

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

    private void Resolve()
    {
        ResolutionPlan plan = PriorityResolver.Resolve(configuration.DefaultGroup, BluetoothRadios.Enumerate());
        if (plan.IsEmpty)
        {
            return;
        }

        // Habilitar antes de deshabilitar: así no hay un instante sin ningún radio.
        if (plan.Enable is not null)
        {
            Apply(plan.Enable, enabled: true);
        }

        foreach (BluetoothRadio radio in plan.Disable)
        {
            Apply(radio, enabled: false);
        }
    }

    private void Apply(BluetoothRadio radio, bool enabled)
    {
        // Un fallo (permisos, dispositivo ocupado) queda registrado y se reintenta en el
        // próximo evento de cambio de dispositivos; la bandeja no se entera.
        if (!DeviceControl.SetEnabled(radio.InstanceId, enabled))
        {
            return;
        }

        Log.Write($"{(enabled ? "Habilitado" : "Deshabilitado")} {radio.Name} ({radio.HardwareId}).");
    }

    private void EnableAllRadios()
    {
        foreach (BluetoothRadio radio in BluetoothRadios.Enumerate().Where(radio => !radio.Enabled))
        {
            Apply(radio, enabled: true);
        }

        notifyIcon.ShowBalloonTip(
            3000,
            "RightBTRadio",
            "Se habilitaron todos los radios. La prioridad vuelve a aplicarse en el próximo cambio de dispositivos.",
            ToolTipIcon.Info);
    }

    private void ShowSettings()
    {
        if (settingsForm is not null)
        {
            settingsForm.Activate();
            return;
        }

        settingsForm = new SettingsForm(configuration);
        settingsForm.FormClosed += (_, _) =>
        {
            settingsForm = null;
            Resolve();
        };
        settingsForm.Show();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            notifyIcon.Visible = false;
            settingsForm?.Dispose();
            menu.Dispose();
            deviceWindow.Dispose();
            notifyIcon.Dispose();
        }

        base.Dispose(disposing);
    }
}
