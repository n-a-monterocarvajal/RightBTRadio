namespace RightBTRadio;

/// <summary>
/// Ventana oculta que recibe <c>WM_DEVICECHANGE</c> y agrupa las ráfagas de cambios
/// en un solo aviso. Deriva de <c>RawInputWindow</c> de RightKeyboard (mismo patrón
/// de <see cref="NativeWindow"/> con temporizador de 200 ms).
/// </summary>
/// <remarks>
/// A diferencia de aquella, la ventana es de nivel superior y no de solo mensajes:
/// <c>DBT_DEVNODES_CHANGED</c> llega por difusión y las ventanas de solo mensajes no
/// reciben difusiones. De nivel superior y nunca mostrada, el mensaje llega sin
/// registrar notificaciones ni describir clases de interfaz.
/// </remarks>
internal sealed class DeviceChangeWindow : NativeWindow, IDisposable
{
    private const int WmDeviceChange = 0x0219;
    private const int DbtDevNodesChanged = 0x0007;

    private readonly System.Windows.Forms.Timer changedTimer = new() { Interval = 200 };

    public DeviceChangeWindow()
    {
        CreateHandle(new CreateParams { Caption = "RightBTRadio.DeviceChange" });
        changedTimer.Tick += OnChangedTimerTick;
    }

    public event Action? DevicesChanged;

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmDeviceChange && (int)message.WParam == DbtDevNodesChanged)
        {
            changedTimer.Stop();
            changedTimer.Start();
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        changedTimer.Stop();
        changedTimer.Tick -= OnChangedTimerTick;
        changedTimer.Dispose();
        DevicesChanged = null;
        DestroyHandle();
    }

    private void OnChangedTimerTick(object? sender, EventArgs e)
    {
        changedTimer.Stop();
        DevicesChanged?.Invoke();
    }
}
