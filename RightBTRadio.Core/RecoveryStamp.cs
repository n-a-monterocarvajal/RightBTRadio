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
        (string Key, DateTimeOffset When)? last = Read();
        if (last is null || !string.Equals(last.Value.Key, key, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return DateTimeOffset.UtcNow - last.Value.When > Cooldown;
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
            string[] parts = File.ReadAllText(FilePath).Split('|');
            if (parts.Length != 3 ||
                !DateTimeOffset.TryParse(
                    parts[2],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset when))
            {
                return null;
            }

            return ($"{parts[0]}|{parts[1]}", when);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
