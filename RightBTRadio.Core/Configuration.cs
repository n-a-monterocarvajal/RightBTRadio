using System.Text.Json;
using System.Text.Json.Serialization;

namespace RightBTRadio;

/// <summary>
/// Un dispositivo asignado a un grupo. La prioridad no se guarda como número: es la
/// posición dentro de <see cref="Configuration.Devices"/>, así que reordenar la lista
/// y renumerar no pueden divergir. La primera posición es la máxima prioridad.
/// </summary>
// No hay un campo de permanencia, interno contra externo, y es deliberado. La lista del
// grupo es un orden total; una clasificación binaria solo puede repetir lo que la lista
// ya dice o contradecirla. Con un interno y un dongle, la presencia del dongle basta,
// porque un dongle desenchufado no se enumera. Con dos internos o dos dongles, el factor
// no distingue nada y decide la lista igual. Windows sí lo expone —BluetoothRadio.Removable
// sale de SPDRP_REMOVAL_POLICY— pero se usa solo para mostrarlo en la ventana.
internal sealed class ConfiguredDevice
{
    public required string HardwareId { get; set; }

    public string Alias { get; set; } = string.Empty;

    /// <summary>Última instancia vista. Solo diagnóstico; el emparejamiento usa el hardware ID.</summary>
    public string InstanceId { get; set; } = string.Empty;
}

internal enum ThemePreference
{
    System,
    Light,
    Dark
}

/// <summary>
/// Preferencias en <c>%LocalAppData%\RightBTRadio\preferences.json</c>. Reproduce el
/// mecanismo de RightKeyboard —camelCase indentado y escritura atómica por archivo
/// temporal— sin sus migraciones: este esquema todavía no tuvo versiones anteriores.
/// </summary>
internal sealed class Configuration
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public int Version { get; set; } = CurrentSchemaVersion;

    /// <summary>Si es falso, la ventana de ajustes se abre al arrancar.</summary>
    public bool StartMinimized { get; set; } = true;

    /// <summary>
    /// Tema de la ventana de ajustes. Campo nuevo con valor por defecto: un archivo sin él
    /// sigue siendo válido, así que no sube la versión del esquema.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ThemePreference>))]
    public ThemePreference Theme { get; set; } = ThemePreference.System;

    /// <summary>El grupo de prioridad. El primero es el de mayor prioridad.</summary>
    public List<ConfiguredDevice> Devices { get; set; } = [];

    public static string GetConfigFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RightBTRadio",
        "preferences.json");

    public static Configuration Load(string? path = null)
    {
        string fullPath = path ?? GetConfigFilePath();
        if (!File.Exists(fullPath))
        {
            return new Configuration();
        }

        Configuration configuration = JsonSerializer.Deserialize<Configuration>(File.ReadAllText(fullPath), JsonOptions)
            ?? throw new InvalidDataException($"«{fullPath}» no contiene una configuración.");

        if (configuration.Version > CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"«{fullPath}» usa el esquema {configuration.Version}. " +
                $"Esta versión admite hasta el esquema {CurrentSchemaVersion}.");
        }

        return configuration;
    }

    /// <summary>
    /// Carga la configuración o, si el archivo no se puede leer, devuelve la de fábrica y
    /// el motivo en <paramref name="error"/>.
    /// </summary>
    public static Configuration LoadOrDefault(out string? error)
    {
        try
        {
            error = null;
            return Load();
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException)
        {
            error = exception.Message;
            Log.Write($"No se pudo cargar la configuración: {error}");
            return new Configuration();
        }
    }

    public void Save(string? path = null)
    {
        string fullPath = Path.GetFullPath(path ?? GetConfigFilePath());
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        Version = CurrentSchemaVersion;

        string temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
