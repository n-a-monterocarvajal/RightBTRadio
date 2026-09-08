using System.Text.Json;
using System.Text.Json.Serialization;

namespace RightBTRadio;

/// <summary>
/// Un dispositivo asignado a un grupo. La prioridad no se guarda como número: es la
/// posición dentro de <see cref="PriorityGroup.Devices"/>, así que reordenar la lista
/// y renumerar no pueden divergir. La primera posición es la máxima prioridad.
/// </summary>
// ponytail: falta un factor de permanencia en el modelo. Hoy la prioridad es lo único
// que decide, y eso supone que los dos radios aparecen a la vez; en la práctica hay uno
// habilitado y otro que se conecta sobre la marcha, y quien tiene dos radios internos ya
// deshabilitó el que no quiere. No hace falta que lo clasifique el usuario: Windows lo
// expone, y se comprobó sobre hardware real que separa los dos casos sin ambigüedad.
// El radio externo informa RemovalPolicy 3 (CM_REMOVAL_POLICY_EXPECT_SURPRISE_REMOVAL),
// InLocalMachineContainer falso y ContainerId propio; el interno informa RemovalPolicy 1
// (CM_REMOVAL_POLICY_EXPECT_NO_REMOVAL), InLocalMachineContainer verdadero y el
// ContainerId de la máquina, {00000000-0000-0000-FFFF-FFFFFFFFFFFF}. El nombre del
// enumerador no sirve: los dos son USB. SPDRP_REMOVAL_POLICY se lee con la misma llamada
// que ya usa BluetoothRadios, y el ContainerId con la clave que usa DeviceIdentityResolver
// de RightKeyboard. Queda por decidir qué hace ese factor con la prioridad, y si el
// usuario puede sobreescribir lo que detecta Windows.
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
