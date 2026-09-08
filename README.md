# RightBTRadio

Utilidad de bandeja para Windows que mantiene habilitado un solo radio Bluetooth
cuando hay varios presentes en el sistema.

Los radios se agrupan y se les asigna una prioridad numérica. Cada vez que cambia
la presencia de dispositivos, la aplicación deja habilitado el radio presente de
mayor prioridad y deshabilita el resto.

Deriva del esqueleto de [RightKeyboard](https://github.com/n-a-monterocarvajal/RightKeyboard)
(bandeja, arranque con Windows, persistencia JSON, enumeración de dispositivos por SetupAPI).

## Requisitos

- .NET 10 SDK (`global.json` fija 10.0.400).
- Windows 11.
- Privilegios de administrador: habilitar y deshabilitar nodos PnP los exige.

## Compilar

```
dotnet build RightBTRadio.slnx
```

## Procedencia y licencia

MIT, archivo `LICENSE`.

El esqueleto viene de [RightKeyboard](https://github.com/n-a-monterocarvajal/RightKeyboard),
que es una obra derivada por capas: la anterior al fork está bajo CPOL 1.02 y los
forks intermedios no declararon licencia; solo los cambios propios del fork son MIT.

RightBTRadio reutiliza únicamente archivos de esa capa MIT, escritos en junio y julio
de 2026: `NativeTrayMenu.cs`, `StartupManager.cs`, `DeviceIdentityResolver.cs`,
`RawInputWindow.cs` y `TrayApplicationContext.cs`.

No se copió nada de la capa anterior al fork. Los dos archivos de RightKeyboard que
proceden de ella —`Program.cs`, importado el 7 de enero de 2020, y `Configuration.cs`,
del 13 de junio de 2020— se reescribieron aquí desde cero. El `Program.cs` de este
repositorio solo comparte con aquel el `[STAThread] static void Main` obligatorio,
y su `Configuration.cs` no comparte modelo ni código.
