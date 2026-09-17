using Microsoft.Win32;

namespace RightBTRadio;

/// <summary>
/// Arranque con Windows por la clave <c>Run</c> de HKCU, como RightKeyboard. La bandeja
/// corre sin elevar, así que el mecanismo nativo sirve y la entrada aparece en
/// Configuración → Aplicaciones → Inicio con su propio interruptor.
/// </summary>
/// <remarks>
/// Tomado de <c>StartupManager</c> de RightKeyboard. La comprobación de
/// <c>StartupApproved</c> existe porque el interruptor de Configuración no borra la
/// entrada de <c>Run</c>: escribe ahí un valor que la marca como deshabilitada.
/// </remarks>
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedKeyPath =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "RightBTRadio";

    /// <summary>
    /// Ruta del ejecutable de bandeja. No se usa <see cref="Environment.ProcessPath"/>
    /// porque quien escribe esta preferencia suele ser la ventana de ajustes, que es
    /// otro proceso.
    /// </summary>
    /// <exception cref="FileNotFoundException">Si no se encuentra el residente.</exception>
    public static string TrayExecutablePath => Executables.FindTray()
        ?? throw new FileNotFoundException("No se encontró el ejecutable de bandeja.", "RightBTRadio.exe");

    /// <remarks>
    /// Consultar el estado nunca falla. Si no se encuentra el residente no puede haber
    /// una entrada válida, y esta propiedad se lee al construir la ventana: lanzar aquí
    /// tumbaría la ventana entera por una preferencia secundaria.
    /// </remarks>
    public static bool IsEnabled
    {
        get
        {
            if (Executables.FindTray() is not string tray)
            {
                return false;
            }

            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (key?.GetValue(ValueName) is not string stored ||
                !string.Equals(stored, Quote(tray), StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            using RegistryKey? approvalKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath);
            return approvalKey?.GetValue(ValueName) is not byte[] approval || IsApproved(approval);
        }
    }

    /// <exception cref="FileNotFoundException">Al activar, si no se encuentra el residente.</exception>
    public static void SetEnabled(bool enabled)
    {
        // Para quitar la entrada no hace falta saber a qué apuntaba; para escribirla sí,
        // y se resuelve antes de abrir la clave para no dejarla a medias si falla.
        string? command = enabled ? Quote(TrayExecutablePath) : null;
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        if (command is null)
        {
            key.DeleteValue(ValueName, false);
            return;
        }

        using RegistryKey? approvalKey = Registry.CurrentUser.OpenSubKey(StartupApprovedKeyPath, true);
        approvalKey?.DeleteValue(ValueName, false);
        key.SetValue(ValueName, command, RegistryValueKind.String);
    }

    private static bool IsApproved(byte[] approval) => approval.Length == 0 || approval[0] != 3;

    private static string Quote(string path) => $"\"{path}\"";
}
