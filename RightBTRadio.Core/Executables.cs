namespace RightBTRadio;

/// <summary>
/// Encuentra los dos ejecutables de la aplicación. Instalados comparten carpeta, así que
/// basta mirar al lado; en desarrollo cada proyecto escribe en la suya y hay que buscar
/// desde la raíz del repositorio.
/// </summary>
/// <remarks>
/// No alcanza con <see cref="AppContext.BaseDirectory"/>: la ventana de ajustes es otro
/// proceso, y cuando ella escribe el arranque con Windows tiene que registrar la ruta del
/// residente, no la suya.
/// </remarks>
internal static class Executables
{
    private const string TrayName = "RightBTRadio.exe";
    private const string SettingsName = "RightBTRadio.WinUI.exe";
    private const string SolutionName = "RightBTRadio.slnx";

    private static readonly string[] Configurations = ["Debug", "Release"];

    // Dos rutas por ejecutable, y no es paranoia: MSBuild intercala la carpeta de
    // plataforma solo cuando el proyecto declara una, así que el residente y la ventana
    // no caen en el mismo sitio. Entre las que existan se toma la más reciente, que es lo
    // que evita medir un binario viejo.
    private static readonly string[] TrayDevelopmentPaths =
    [
        @"RightBTRadio\bin\{0}\net10.0-windows",
        @"RightBTRadio\bin\x64\{0}\net10.0-windows"
    ];

    private static readonly string[] SettingsDevelopmentPaths =
    [
        @"RightBTRadio.WinUI\bin\{0}\net10.0-windows10.0.19041.0\win-x64",
        @"RightBTRadio.WinUI\bin\x64\{0}\net10.0-windows10.0.19041.0\win-x64"
    ];

    public static string? FindTray() => Find(TrayName, TrayDevelopmentPaths);

    public static string? FindSettings() => Find(SettingsName, SettingsDevelopmentPaths);

    private static string? Find(string name, IReadOnlyList<string> developmentPaths)
    {
        string beside = Path.Combine(AppContext.BaseDirectory, name);
        if (File.Exists(beside))
        {
            return beside;
        }

        // Instalaciones anteriores a que los dos ejecutables compartieran carpeta.
        string legacy = Path.Combine(AppContext.BaseDirectory, "ui", name);
        if (File.Exists(legacy))
        {
            return legacy;
        }

        // ponytail: rutas del árbol de compilación. Sobran en cuanto haya instalador.
        if (FindRepositoryRoot() is not string root)
        {
            return null;
        }

        // La configuración no se deduce de la ruta propia: el residente y la ventana
        // tienen distinta profundidad de carpetas, así que se prueban las dos.
        string? found = Configurations
            .SelectMany(configuration => developmentPaths.Select(
                pattern => Path.Combine(root, string.Format(pattern, configuration), name)))
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (found is null)
        {
            Log.Write($"No se encontró {name} ni junto al ejecutable ni en el árbol de compilación.");
        }

        return found;
    }

    /// <summary>Sube hasta el directorio que contiene la solución, o nulo si no hay.</summary>
    private static string? FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
