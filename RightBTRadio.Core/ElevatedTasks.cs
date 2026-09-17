using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace RightBTRadio;

/// <summary>
/// Tareas programadas que ejecutan el trabajo que exige elevación. La bandeja corre sin
/// elevar, así que arranca por la clave <c>Run</c> y aparece en Configuración →
/// Aplicaciones → Inicio; cuando hay que tocar un nodo PnP dispara una de estas tareas,
/// registrada con <c>RunLevel HighestAvailable</c>, y Windows la eleva sin pedir UAC.
/// </summary>
/// <remarks>
/// Son dos tareas y no una con argumentos porque <c>schtasks /Run</c> no permite pasar
/// argumentos a la acción.
/// </remarks>
internal static class ElevatedTasks
{
    public const string ApplyTaskName = "RightBTRadio.Apply";
    public const string EnableAllTaskName = "RightBTRadio.EnableAll";

    /// <summary>
    /// Verdadero solo si las dos tareas existen y ejecutan el residente actual. No basta
    /// con que existan: tras instalar, mover o actualizar la aplicación, unas tareas
    /// viejas seguirían apuntando al ejecutable anterior.
    /// </summary>
    public static bool AreRegistered
    {
        get
        {
            string? tray = Executables.FindTray();
            return tray is not null && PointsAt(ApplyTaskName, tray) && PointsAt(EnableAllTaskName, tray);
        }
    }


    /// <summary>Ejecutable que la tarea lanza, o nulo si no está registrada.</summary>
    internal static string? RegisteredExecutable(string taskName)
    {
        if (RunSchtasks(out _, out string xml, "/Query", "/TN", taskName, "/XML") != 0)
        {
            return null;
        }

        return ParseCommand(xml);
    }

    /// <summary>
    /// Extrae el ejecutable del XML de una tarea. Se lee del XML y no de <c>/FO LIST</c>
    /// porque los nombres de campo de schtasks están traducidos y cambian con el idioma
    /// de Windows; las etiquetas del XML no.
    /// </summary>
    internal static string? ParseCommand(string xml)
    {
        Match command = Regex.Match(xml, "<Command>(?<path>.*?)</Command>", RegexOptions.Singleline);
        return command.Success
            ? Environment.ExpandEnvironmentVariables(command.Groups["path"].Value.Trim())
            : null;
    }

    private static bool PointsAt(string taskName, string executable) =>
        string.Equals(RegisteredExecutable(taskName), executable, StringComparison.OrdinalIgnoreCase);

    /// <summary>Lanza la tarea y devuelve verdadero si Windows aceptó ejecutarla.</summary>
    public static bool Run(string taskName)
    {
        int result = RunSchtasks(out string error, "/Run", "/TN", taskName);
        if (result != 0)
        {
            Log.Write($"No se pudo ejecutar la tarea {taskName} (código {result}): {error}");
            return false;
        }

        return true;
    }

    /// <summary>Registra las dos tareas. Requiere privilegios de administrador.</summary>
    public static void Register(string executablePath)
    {
        Register(ApplyTaskName, executablePath, "--apply", "Aplica la prioridad entre radios Bluetooth.");
        Register(EnableAllTaskName, executablePath, "--enable-all", "Habilita todos los radios Bluetooth.");
    }

    public static void Unregister()
    {
        RunSchtasks(out _, "/Delete", "/TN", ApplyTaskName, "/F");
        RunSchtasks(out _, "/Delete", "/TN", EnableAllTaskName, "/F");
    }

    private static void Register(string taskName, string executablePath, string arguments, string description)
    {
        string definition = Path.Combine(Path.GetTempPath(), $"{taskName}-{Guid.NewGuid():N}.xml");
        try
        {
            // schtasks /Create /XML exige UTF-16.
            File.WriteAllText(definition, BuildDefinition(executablePath, arguments, description), Encoding.Unicode);
            int result = RunSchtasks(out string error, "/Create", "/TN", taskName, "/XML", definition, "/F");
            if (result != 0)
            {
                throw new InvalidOperationException($"schtasks no pudo registrar {taskName} (código {result}). {error}");
            }
        }
        finally
        {
            if (File.Exists(definition))
            {
                File.Delete(definition);
            }
        }
    }

    private static string BuildDefinition(string executablePath, string arguments, string description)
    {
        string user = $@"{Environment.UserDomainName}\{Environment.UserName}";
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>{Escape(description)}</Description>
              </RegistrationInfo>
              <Principals>
                <Principal id="Author">
                  <UserId>{Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <!-- Solo lo que difiere de los valores por omisión del Programador de tareas. -->
                <MultipleInstancesPolicy>Queue</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT2M</ExecutionTimeLimit>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{Escape(executablePath)}</Command>
                  <Arguments>{Escape(arguments)}</Arguments>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static readonly Lazy<Encoding> OemEncoding = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding((int)GetOEMCP());
    });

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();

    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? value;

    private static int RunSchtasks(out string error, params string[] arguments) =>
        RunSchtasks(out error, out _, arguments);

    private static int RunSchtasks(out string error, out string output, params string[] arguments)
    {
        // Redirigido, schtasks escribe en la página de códigos OEM de la consola que recibe,
        // incluso el XML que declara encoding="UTF-16". Leerlo como UTF-16 dejaba el XML
        // ilegible, AreRegistered daba siempre falso y la bandeja pedía UAC en cada inicio.
        // Medido: una ruta con «ñ» llega como 0xA4, la ñ de CP850.
        Encoding consoleEncoding = OemEncoding.Value;
        ProcessStartInfo startInfo = new(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = consoleEncoding,
            StandardErrorEncoding = consoleEncoding
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            error = "no se pudo iniciar schtasks.exe";
            output = string.Empty;
            return -1;
        }

        error = process.StandardError.ReadToEnd().Trim();
        output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }
}
