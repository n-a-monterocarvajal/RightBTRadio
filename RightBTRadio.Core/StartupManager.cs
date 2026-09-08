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
    /// Ruta del ejecutable de bandeja. Se resuelve por carpeta y no por
    /// <see cref="Environment.ProcessPath"/> porque la ventana de ajustes es otro
    /// proceso y se publica en la misma carpeta.
    /// </summary>
    public static string TrayExecutablePath => Path.Combine(AppContext.BaseDirectory, "RightBTRadio.exe");

    public static bool IsEnabled =>
        IsEnabledCore(RunKeyPath, StartupApprovedKeyPath, ValueName, BuildCommand());

    public static void SetEnabled(bool enabled) =>
        SetEnabledCore(RunKeyPath, StartupApprovedKeyPath, ValueName, BuildCommand(), enabled);

    // Núcleo verificable: opera sobre HKCU con rutas, valor y comando explícitos para
    // poder aislarlo en pruebas sin tocar la clave Run real del usuario.
    internal static bool IsEnabledCore(
        string runKeyPath,
        string approvedKeyPath,
        string valueName,
        string command)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(runKeyPath);
        if (key?.GetValue(valueName) is not string stored ||
            !string.Equals(stored, command, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        using RegistryKey? approvalKey = Registry.CurrentUser.OpenSubKey(approvedKeyPath);
        return approvalKey?.GetValue(valueName) is not byte[] approval || IsApproved(approval);
    }

    internal static void SetEnabledCore(
        string runKeyPath,
        string approvedKeyPath,
        string valueName,
        string command,
        bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(runKeyPath, true);
        if (enabled)
        {
            using RegistryKey? approvalKey = Registry.CurrentUser.OpenSubKey(approvedKeyPath, true);
            approvalKey?.DeleteValue(valueName, false);
            key.SetValue(valueName, command, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(valueName, false);
        }
    }

    private static bool IsApproved(byte[] approval) => approval.Length == 0 || approval[0] != 3;

    private static string BuildCommand() => $"\"{TrayExecutablePath}\"";
}
