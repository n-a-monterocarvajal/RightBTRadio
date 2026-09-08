# RightBTRadio

Utilidad para Windows que mantiene habilitado un solo radio Bluetooth cuando hay
varios presentes en el sistema.

Los radios se agrupan y se les asigna una prioridad. Cada vez que cambia la presencia
de dispositivos, la aplicación deja habilitado el radio presente de mayor prioridad y
deshabilita el resto.

Deriva del esqueleto de [RightKeyboard](https://github.com/n-a-monterocarvajal/RightKeyboard).

## Estructura

| Proyecto | Qué contiene |
|---|---|
| `RightBTRadio.Core` | Enumeración de radios, control de nodos PnP, configuración, resolución de prioridad, tareas programadas y contrato visual. Sin interfaz. |
| `RightBTRadio` | Residente del área de notificación (WinForms) y los modos sin interfaz. Corre sin elevar. |
| `RightBTRadio.WinUI` | Ventana de ajustes en WinUI 3, con Mica. Proceso aparte, bajo demanda. |
| `RightBTRadio.Tests` | Pruebas de la resolución de prioridad y del filtro de enumeradores. |

El residente nunca escribe la configuración: solo la lee. La ventana de ajustes es la
única que escribe. Por eso no hace falta el IPC que sí necesita RightKeyboard.

## Elevación

Habilitar y deshabilitar nodos PnP exige privilegios de administrador. El residente
igualmente corre sin elevar, para poder arrancar desde la clave `Run` y aparecer en
Configuración → Aplicaciones → Inicio con su propio interruptor.

El trabajo elevado lo hacen dos tareas programadas con `RunLevel HighestAvailable`,
`RightBTRadio.Apply` y `RightBTRadio.EnableAll`, que el residente dispara con
`schtasks /Run`. Se registran una sola vez, con una elevación explícita; después de eso
Windows no vuelve a pedir UAC.

Son dos tareas y no una con argumentos porque `schtasks /Run` no permite pasar
argumentos a la acción.

## Requisitos

- .NET 10 SDK (`global.json` fija 10.0.400).
- Windows 11.
- [winapp CLI](https://github.com/microsoft/winappcli), solo para el arnés de interfaz.

## Compilar y probar

```
dotnet build RightBTRadio.slnx
dotnet test RightBTRadio.slnx
```

## Modos sin interfaz

Existen para probar durante el desarrollo y para que las tareas programadas tengan qué
ejecutar. Los dos primeros exigen elevación.

```
RightBTRadio.exe --apply              # resuelve la prioridad y sale
RightBTRadio.exe --enable-all         # habilita todos los radios y sale
RightBTRadio.exe --list               # lista los radios presentes y su estado
RightBTRadio.exe --register-tasks     # registra las dos tareas programadas
RightBTRadio.exe --unregister-tasks   # las elimina
```

## Arnés de interfaz

```
pwsh -File scripts/ui-harness.ps1
```

Compila la ventana de ajustes, la levanta, afirma lo que promete
`RightBTRadio.Core/SettingsVisualContract.cs`, deja una captura en
`artifacts/ui-harness/` y cierra lo que abrió. Sale 0 si todo se afirmó, 1 si algo falló.

| Parámetro | Para qué |
|---|---|
| `-Configuration` | `Debug` por omisión; `Release` para revisar lo que se publica |
| `-Attach` | Mide una ventana ya abierta en lugar de lanzarla, y no la cierra |
| `-KeepOpen` | Deja la aplicación viva al terminar |
| `-SkipEvidence` | Omite la captura |
| `-EvidenceDirectory` | Cambia dónde se deja la captura |

Necesita una sesión interactiva de Windows: `winapp ui` opera sobre UI Automation.

Los textos esperados se leen del contrato en cada corrida, no se copian al script, así
que un cambio de contrato sin cambio de interfaz —o al revés— rompe el arnés a
propósito. Comprobado: al desviar el subtítulo de la ventana respecto del contrato, el
arnés falla esa aserción y sale 1.

El arnés usa solo los verbos que no inyectan entrada. La captura no es una aserción
sino evidencia; es lo primero que se puede omitir.

Lo que no resuelve: si Mica se está viendo de verdad —Windows cae a color sólido sin
avisar a la aplicación, y la captura sirve para desempatarlo a ojo—, la alineación y los
estados visuales, y la conexión o desconexión física de un radio.

## Procedencia y licencia

MIT, archivo `LICENSE`.

El esqueleto viene de RightKeyboard, que es una obra derivada por capas: la anterior al
fork está bajo CPOL 1.02 y los forks intermedios no declararon licencia; solo los
cambios propios del fork son MIT.

RightBTRadio reutiliza únicamente archivos de esa capa MIT, escritos en junio y julio
de 2026: `NativeTrayMenu.cs`, `StartupManager.cs`, `DeviceIdentityResolver.cs`,
`RawInputWindow.cs` y `TrayApplicationContext.cs`, más los criterios de
`SettingsPanelVisualContract.cs` y del arnés de interfaz.

No se copió nada de la capa anterior al fork. Los dos archivos de RightKeyboard que
proceden de ella —`Program.cs`, importado el 7 de enero de 2020, y `Configuration.cs`,
del 13 de junio de 2020— se reescribieron aquí desde cero. El `Program.cs` de este
repositorio solo comparte con aquel el `[STAThread] static void Main` obligatorio, y su
`Configuration.cs` no comparte modelo ni código.
