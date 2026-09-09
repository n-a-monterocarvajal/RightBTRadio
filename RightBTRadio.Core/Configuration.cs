using System.Text.Json;
using System.Text.Json.Serialization;

namespace RightBTRadio;

/// <summary>
/// Un dispositivo asignado a un grupo. La prioridad no se guarda como número: es la
/// posición dentro de <see cref="PriorityGroup.Devices"/>, así que reordenar la lista
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

internal sealed class PriorityGroup
{
    public required string Id { get; set; }

    public required string Name { get; set; }

    public List<ConfiguredDevice> Devices { get; set; } = [];
}

/// <summary>
/// Preferencias en <c>%LocalAppData%\RightBTRadio\preferences.json</c>. Reproduce el
/// mecanismo de RightKeyboard —camelCase indentado y escritura atómica por archivo
/// temporal— sin sus migraciones: este esquema todavía no tuvo versiones anteriores.
/// </summary>
internal sealed class Configuration
{
    public const int CurrentSchemaVersion = 1;
    public const string DefaultGroupId = "bluetooth-radios";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public int Version { get; set; } = CurrentSchemaVersion;

    /// <summary>Si es falso, la ventana de ajustes se abre al arrancar.</summary>
    public bool StartMinimized { get; set; } = true;

    public List<PriorityGroup> Groups { get; set; } = [];

    /// <summary>
    /// El grupo único que expone la interfaz hoy. El modelo admite varios; la interfaz
    /// todavía no los necesita.
    /// </summary>
    [JsonIgnore]
    public PriorityGroup DefaultGroup
    {
        get
        {
            PriorityGroup? group = Groups.FirstOrDefault(candidate => candidate.Id == DefaultGroupId);
            if (group is null)
            {
                group = new PriorityGroup { Id = DefaultGroupId, Name = "Radios Bluetooth" };
                Groups.Add(group);
            }

            return group;
        }
    }

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
