using System.Globalization;

namespace RightBTRadio;

/// <summary>
/// Anota el último intento de reiniciar el nodo de un radio, para no repetirlo en
/// bucle. Reiniciar un nodo genera cambios de dispositivo, que disparan otra
/// resolución; si el reinicio no arregla el problema, sin esta marca la aplicación
/// estaría encendiendo y apagando el radio del usuario para siempre.
/// </summary>
internal static class RecoveryStamp
{
    // Perilla de calibración: cuánto esperar antes de volver a intentar el mismo
    // reinicio. Corto deja lugar a un bucle; largo deja al usuario esperando tras
    // reconectar el radio.
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(2);

    private static string FilePath => Path.Combine(
        Path.GetDirectoryName(Configuration.GetConfigFilePath())!,
        "recovery.txt");

    /// <summary>
    /// Verdadero si conviene intentar el reinicio. Se salta solo si ya se intentó lo
    /// mismo —igual radio, igual código de problema— hace poco.
    /// </summary>
    public static bool ShouldAttempt(string hardwareId, uint problem)
    {
        string key = $"{hardwareId}|{problem}";
        return Read() is not (string lastKey, DateTimeOffset when) ||
            !string.Equals(lastKey, key, StringComparison.OrdinalIgnoreCase) ||
            DateTimeOffset.UtcNow - when > Cooldown;
    }

    public static void Record(string hardwareId, uint problem)
    {
        try
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                $"{hardwareId}|{problem}|{DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture)}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Sin marca se puede reintentar de más, pero no se puede dejar de resolver.
        }
    }

    private static (string Key, DateTimeOffset When)? Read()
    {
        try
        {
            // La marca es «clave|fecha», y la clave ya lleva su propio separador.
            string stamp = File.ReadAllText(FilePath);
            int separator = stamp.LastIndexOf('|');
            return separator > 0 && DateTimeOffset.TryParse(
                stamp[(separator + 1)..],
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out DateTimeOffset when)
                ? (stamp[..separator], when)
                : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
