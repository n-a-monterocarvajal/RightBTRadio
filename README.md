# RightBTRadio

Utilidad para Windows que mantiene habilitado un solo radio Bluetooth cuando hay
varios conectados.

Para esto, se propone un orden de dispositivos por prioridad: cada vez que se conecta o se
desconecta un dispositivo, la aplicación deja habilitado el primer radio conectado del
grupo y deshabilita los demás.

![Ventana de ajustes de RightBTRadio](docs/images/ajustes.png)

## Descarga

La última versión está en [Releases](https://github.com/n-a-monterocarvajal/RightBTRadio/releases).
Descargue `RightBTRadio-<versión>-Setup.exe` y ejecútelo. El instalador pide permiso
de administrador una vez.

## Por qué existe

Windows no puede usar dos radios Bluetooth a la vez (aunque sí maneja dos adaptadores
Wi-Fi): con los dos conectados, uno queda con el código de problema 31,
`CM_PROB_FAILED_INSTALL`, porque Windows no carga su controlador. Así, por ejemplo, si conecta un
adaptador USB a un equipo con Bluetooth integrado, uno de los dos deja de funcionar, y
la única solución manual es entrar al Administrador de dispositivos cada vez.

Deshabilitar el radio que pierde no alcanza, porque el nodo del ganador no se recupera
solo cuando desaparece el conflicto. En pruebas con hardware real,
`CM_Reenumerate_DevNode` con `CM_REENUMERATE_RETRY_INSTALLATION` dejó el problema 31
intacto. Deshabilitar y volver a habilitar el nodo del ganador lo llevó a 0 en unos
segundos.

Sigue como tarea pendiente comprobar si ese ciclo es la única vía para superar el conflicto. Un comentario en
`RadioService.cs` enumera las alternativas sin probar, y hay un
[issue abierto](https://github.com/n-a-monterocarvajal/RightBTRadio/issues/1) para
investigarlas.

## Instalación

El instalador pesa 7,7 MB. Si faltan el .NET Desktop Runtime o el Windows App Runtime,
los descarga con los instaladores oficiales de Microsoft. La aplicación queda en `%ProgramFiles%\RightBTRadio`, y el instalador
registra las dos tareas programadas que operan con privilegios de
administrador.

Requiere Windows 10 versión 1809 o posterior, de 64 bits. Solo se probó en Windows 11.

Al desinstalar, primero se habilitan todos los radios y después se borran los
archivos. Así, quien desinstale con un radio deshabilitado no se queda sin él.

## Interno y externo

Windows indica sin ambigüedad si un radio viene integrado o se puede quitar. Estos son
los valores medidos en hardware real:

| | Radio externo | Radio interno |
|---|---|---|
| `RemovalPolicy` | 3, `CM_REMOVAL_POLICY_EXPECT_SURPRISE_REMOVAL` | 1, `CM_REMOVAL_POLICY_EXPECT_NO_REMOVAL` |
| `InLocalMachineContainer` | falso | verdadero |
| `ContainerId` | propio | `{00000000-0000-0000-FFFF-FFFFFFFFFFFF}` |
| `EnumeratorName` | USB | USB |

En consecuencia, la política de extracción sirve para distinguirlos. `BluetoothRadios` la lee.

**La lista de prioridad no usa este dato**.

El estado del sistema sí influye en el orden inicial. Al agregar un radio habilitado,
entra por encima de los que están deshabilitados, pues se asume que un radio que el usuario
deshabilitó por su cuenta indica una preferencia. Después, el orden lo decide el
usuario con los botones `Subir` y `Bajar`.

## Permisos de administrador

Windows exige privilegios de administrador para habilitar y deshabilitar nodos PnP.
Aun así, el residente se ejecuta sin elevación. De ese modo puede arrancar desde la
clave `Run` y aparece en Configuración → Aplicaciones → Inicio con su propio
interruptor.

El trabajo con privilegios lo hacen dos tareas programadas con
`RunLevel HighestAvailable`: `RightBTRadio.Apply` y `RightBTRadio.EnableAll`. El
residente las lanza con `schtasks /Run`. El instalador las registra, y si faltan, la
aplicación pide permiso una vez para registrarlas. Las tareas son por `usuario`, así que
otro usuario del mismo equipo las registra al abrir la aplicación.

Se utilizan dos tareas, en lugar de una con argumentos, pues `schtasks /Run` no permite pasar
argumentos a la acción.

## Estructura

| Proyecto | Contenido |
|---|---|
| `RightBTRadio.Core` | Enumeración de radios, control de nodos PnP, ajustes, resolución de prioridad, tareas programadas y contrato visual. No tiene interfaz. |
| `RightBTRadio` | Residente del área de notificación (WinForms) y modos de línea de comandos. Se ejecuta sin elevación. |
| `RightBTRadio.WinUI` | Ventana de ajustes en WinUI 3, con Mica. Es un proceso aparte que se abre cuando se necesita. |
| `RightBTRadio.Tests` | Pruebas de la resolución de prioridad, del filtro de enumeradores, de los ajustes y de la lectura de tareas. |

Los ajustes se guardan en `%LocalAppData%\RightBTRadio\preferences.json`. Solo la
ventana de ajustes los escribe; el residente solo los lee.

## Compilar y probar

```
dotnet build RightBTRadio.slnx
dotnet test RightBTRadio.slnx
```

## Modos de línea de comandos

Útiles para pruebas de desarrollo.

```
RightBTRadio.exe --apply              # aplica la prioridad y sale
RightBTRadio.exe --enable-all         # habilita todos los radios y sale
RightBTRadio.exe --list               # muestra los radios detectados y su código de estado
RightBTRadio.exe --register-tasks     # registra las dos tareas programadas
RightBTRadio.exe --unregister-tasks   # elimina las tareas
```

## Arnés de interfaz

```
pwsh -File scripts/ui-harness.ps1
```

El arnés compila la solución, abre la ventana de ajustes y comprueba lo que declara
`RightBTRadio.Core/SettingsVisualContract.cs`. Guarda una captura en
`artifacts/ui-harness/` y luego cierra la instancia que abrió. Devuelve `0` si todas las comprobaciones
pasan y `1` si alguna falla.

| Parámetro | Uso |
|---|---|
| `-Configuration` | `Debug` por omisión; `Release` para revisar lo que se publica |
| `-Attach` | Usa una ventana ya abierta en lugar de abrir otra, y no la cierra |
| `-KeepOpen` | Deja la aplicación abierta al terminar |
| `-SkipEvidence` | No guarda la captura |
| `-EvidenceDirectory` | Cambia la carpeta de la captura |

Necesita una sesión interactiva de Windows, porque `winapp ui` usa UI Automation, y el
[CLI winapp](https://github.com/microsoft/winappcli).

El script lee los textos esperados del contrato en cada ejecución en lugar de
copiarlos. Si el contrato cambia y la interfaz no, o al revés, el arnés falla.

El arnés solo usa comandos que no simulan entrada del usuario. La captura sirve como
evidencia y no forma parte de las comprobaciones, así que se puede omitir.

## Generar el instalador

```
pwsh -File scripts/build-installer.ps1
```

El script publica los dos ejecutables en una misma carpeta, dependientes del marco, y
descarta los 40 MB de Windows ML que el Windows App SDK incluye aunque la aplicación
no los usa. Después compila el instalador con Inno Setup y deja el resultado y su
SHA-256 en `artifacts/installer/`.

Con `-SkipInstaller`, publica y verifica sin ejecutar Inno Setup.

El icono se genera con código en lugar de guardarse como binario:

```
pwsh -File scripts/make-icon.ps1
```

## Origen y licencia

MIT. Consulte el archivo `LICENSE`.

La estructura base viene de [RightKeyboard](https://github.com/n-a-monterocarvajal/RightKeyboard),
que es una obra derivada en capas. La capa anterior al fork usa la licencia CPOL 1.02,
los forks intermedios no declararon licencia y solo los cambios propios del fork son
MIT.

RightBTRadio reutiliza solo archivos de la capa MIT, escritos en junio y julio de 2026:
`NativeTrayMenu.cs`, `StartupManager.cs`, `DeviceIdentityResolver.cs`,
`RawInputWindow.cs` y `TrayApplicationContext.cs`. También toma los criterios de
`SettingsPanelVisualContract.cs`, del arnés de interfaz y del instalador.

Nada se copió de la capa anterior al fork. Los dos archivos de RightKeyboard que
vienen de esa capa, `Program.cs` (importado el 7 de enero de 2020) y
`Configuration.cs` (del 13 de junio de 2020), se escribieron aquí desde cero. El
`Program.cs` de este repositorio solo comparte con aquel el
`[STAThread] static void Main` obligatorio, y `Configuration.cs` no comparte modelo ni
código.
