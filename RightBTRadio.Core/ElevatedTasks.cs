using System.Diagnostics;
using System.Text;

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

    public static bool AreRegistered => Exists(ApplyTaskName) && Exists(EnableAllTaskName);

    public static bool Exists(string taskName) => RunSchtasks(out _, "/Query", "/TN", taskName) == 0;

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
                <MultipleInstancesPolicy>Queue</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <WakeToRun>false</WakeToRun>
                <ExecutionTimeLimit>PT2M</ExecutionTimeLimit>
                <Priority>7</Priority>
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

    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? value;

    private static int RunSchtasks(out string error, params string[] arguments)
    {
        ProcessStartInfo startInfo = new(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            error = "no se pudo iniciar schtasks.exe";
            return -1;
        }

        error = process.StandardError.ReadToEnd().Trim();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }
}
