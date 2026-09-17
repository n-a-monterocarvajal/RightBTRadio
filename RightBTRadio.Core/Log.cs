namespace RightBTRadio;

/// <summary>
/// Registro mínimo junto a la configuración. Solo lo usan las rutas que pueden
/// fallar sin que el usuario esté mirando: habilitar o deshabilitar un radio.
/// </summary>
internal static class Log
{
    public static string FilePath => Path.Combine(
        Path.GetDirectoryName(Configuration.GetConfigFilePath())!,
        "log.txt");

    public static void Write(string message)
    {
        try
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTime.Now:s} {message}{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Un registro que no se puede escribir no debe tumbar la bandeja.
        }
    }
}
