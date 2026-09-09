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

    [Test]
    public void DevuelveNuloSiElXmlNoTraeAccion()
    {
        Assert.That(ElevatedTasks.ParseCommand("<Task></Task>"), Is.Null);
        Assert.That(ElevatedTasks.ParseCommand(string.Empty), Is.Null);
    }
}
