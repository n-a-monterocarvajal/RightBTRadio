using NUnit.Framework;

namespace RightBTRadio.Tests;

[TestFixture]
public sealed class ElevatedTasksTests
{
    private const string TaskXml = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <Actions Context="Author">
            <Exec>
              <Command>C:\Users\alguien\AppData\Local\RightBTRadio\app\RightBTRadio.exe</Command>
              <Arguments>--apply</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;

    [Test]
    public void LeeElEjecutableDelXmlDeLaTarea()
    {
        Assert.That(
            ElevatedTasks.ParseCommand(TaskXml),
            Is.EqualTo(@"C:\Users\alguien\AppData\Local\RightBTRadio\app\RightBTRadio.exe"));
    }

    [Test]
    public void ExpandeLasVariablesDeEntorno()
    {
        string xml = TaskXml.Replace(
            @"C:\Users\alguien\AppData\Local",
            "%LOCALAPPDATA%",
            StringComparison.Ordinal);

        Assert.That(
            ElevatedTasks.ParseCommand(xml),
            Is.EqualTo(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"RightBTRadio\app\RightBTRadio.exe")));
    }

    /// <summary>
    /// Contra schtasks real: su salida redirigida no es UTF-16, y leerla así hacía que la
    /// bandeja pidiera registrar las tareas en cada inicio. La tarea de prueba no pide
    /// elevación y se borra al terminar.
    /// </summary>
    [Test]
    public void LeeLaRutaDeUnaTareaRegistradaConCaracteresNoAscii()
    {
        const string taskName = "RightBTRadio.Tests.Encoding";
        const string executable = @"C:\Configuración\ñandú\RightBTRadio.exe";
        Assert.That(Schtasks("/Create", "/TN", taskName, "/TR", executable, "/SC", "ONCE", "/ST", "23:59", "/F"), Is.Zero);
        try
        {
            Assert.That(ElevatedTasks.RegisteredExecutable(taskName), Is.EqualTo(executable));
        }
        finally
        {
            Schtasks("/Delete", "/TN", taskName, "/F");
        }
    }

    private static int Schtasks(params string[] arguments)
    {
        System.Diagnostics.ProcessStartInfo startInfo = new("schtasks.exe") { CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    [Test]
    public void DevuelveNuloSiElXmlNoTraeAccion()
    {
        Assert.That(ElevatedTasks.ParseCommand("<Task></Task>"), Is.Null);
        Assert.That(ElevatedTasks.ParseCommand(string.Empty), Is.Null);
    }
}
