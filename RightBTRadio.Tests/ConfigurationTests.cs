using NUnit.Framework;

namespace RightBTRadio.Tests;

[TestFixture]
public sealed class ConfigurationTests
{
    [Test]
    public void GuardaElTemaPorNombreYLeeArchivosQueNoLoTraen()
    {
        string path = Path.Combine(Path.GetTempPath(), $"RightBTRadio-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """{ "version": 1, "startMinimized": true, "devices": [] }""");
            Assert.That(Configuration.Load(path).Theme, Is.EqualTo(ThemePreference.System));

            new Configuration { Theme = ThemePreference.Dark }.Save(path);
            Assert.That(File.ReadAllText(path), Does.Contain("\"theme\": \"Dark\""));
            Assert.That(Configuration.Load(path).Theme, Is.EqualTo(ThemePreference.Dark));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
