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
