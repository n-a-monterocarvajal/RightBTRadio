using System.Diagnostics;
using System.Text;

namespace RightBTRadio;

/// <summary>
/// Arranque con Windows mediante una tarea programada, no la clave <c>Run</c> que usa
/// RightKeyboard. La aplicación se ejecuta elevada para poder habilitar y deshabilitar
/// nodos PnP, y un ejecutable elevado lanzado desde <c>Run</c> muestra el aviso de UAC
/// en cada inicio de sesión. La tarea con <c>RunLevel HighestAvailable</c> arranca
/// elevada sin aviso.
/// </summary>
internal static class StartupManager
{
    private const string TaskName = "RightBTRadio";

    public static bool IsEnabled => RunSchtasks("/Query", "/TN", TaskName) == 0;

    public static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            RunSchtasks("/Delete", "/TN", TaskName, "/F");
            return;
        }

        string definition = Path.Combine(Path.GetTempPath(), $"RightBTRadio-{Guid.NewGuid():N}.xml");
        try
        {
            // schtasks /Create /XML exige UTF-16.
            File.WriteAllText(definition, BuildTaskDefinition(), Encoding.Unicode);
            int result = RunSchtasks("/Create", "/TN", TaskName, "/XML", definition, "/F");
            if (result != 0)
            {
                throw new InvalidOperationException(
                    $"schtasks no pudo registrar la tarea de arranque (código {result}).");
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

    private static string BuildTaskDefinition()
    {
        string user = $@"{Environment.UserDomainName}\{Environment.UserName}";
        string executable = Environment.ProcessPath ?? Application.ExecutablePath;
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Mantiene habilitado solo el radio Bluetooth de mayor prioridad.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{Escape(user)}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{Escape(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
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
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{Escape(executable)}</Command>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static string Escape(string value) => System.Security.SecurityElement.Escape(value) ?? value;

    private static int RunSchtasks(params string[] arguments)
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
            return -1;
        }

        string error = process.StandardError.ReadToEnd();
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 && arguments[0] != "/Query")
        {
            Log.Write($"schtasks {arguments[0]} devolvió {process.ExitCode}: {error.Trim()}");
        }

        return process.ExitCode;
    }
}
