# RightBTRadio

Utilidad para Windows que mantiene habilitado un solo radio Bluetooth cuando hay
varios presentes en el sistema.

Los radios se agrupan y se les asigna una prioridad. Cada vez que cambia la presencia
de dispositivos, la aplicación deja habilitado el radio presente de mayor prioridad y
deshabilita el resto.

## Por qué existe

Windows no maneja dos radios Bluetooth a la vez, a diferencia de lo que hace con dos
antenas Wi-Fi. Con los dos presentes, uno queda con el código de problema 31,
`CM_PROB_FAILED_INSTALL`: Windows no carga su controlador. Quien conecta un adaptador
USB teniendo uno integrado se encuentra con que uno de los dos deja de servir, y con
que resolverlo a mano es entrar al Administrador de dispositivos cada vez.

Deshabilitar al radio perdedor no basta: el nodo del ganador no se recupera solo
cuando el conflicto desaparece. Medido sobre hardware real, `CM_Reenumerate_DevNode`
con `CM_REENUMERATE_RETRY_INSTALLATION` deja el problema 31 intacto; un ciclo de
deshabilitar y volver a habilitar el nodo del ganador lo lleva a 0 en unos segundos.
Por eso `--apply` termina con ese ciclo cuando el ganador quedó con un problema.

Queda pendiente comprobar si ese ciclo es la única vía. El código lo anota con un
comentario `ponytail:` que enumera las alternativas sin probar.

## Instalación

Descargue `RightBTRadio-<versión>-Setup.exe` y ejecútelo. Pide elevación una vez.

El instalador es liviano, 7,7 MB. Si faltan el .NET Desktop Runtime o el Windows App
Runtime, los descarga de los bootstrappers oficiales de Microsoft; en una máquina que
ya los tiene no descarga nada. Instala en `%ProgramFiles%\RightBTRadio` y registra las
dos tareas programadas que hacen el trabajo elevado, así que la aplicación no vuelve a
pedir permisos.

Requiere Windows 11 x64. Aprovechando esa misma elevación, la desinstalación devuelve
todos los radios a su estado habilitado antes de borrar nada: quien desinstale con un
radio deshabilitado no se queda sin él.

## Interno contra externo

Windows distingue un radio integrado de uno desconectable sin ambigüedad. Medido sobre
hardware real:

| | Radio externo | Radio interno |
|---|---|---|
| `RemovalPolicy` | 3, `CM_REMOVAL_POLICY_EXPECT_SURPRISE_REMOVAL` | 1, `CM_REMOVAL_POLICY_EXPECT_NO_REMOVAL` |
| `InLocalMachineContainer` | falso | verdadero |
| `ContainerId` | propio | `{00000000-0000-0000-FFFF-FFFFFFFFFFFF}` |
| `EnumeratorName` | USB | USB |

El nombre del enumerador no sirve para distinguirlos; la política de extracción sí, y
es la que lee `BluetoothRadios`.

**Ese dato no interviene en la prioridad, y no es un descuido.** La lista del grupo es
un orden total; una clasificación binaria solo puede repetir lo que la lista ya dice o
contradecirla. Con un interno y un dongle, la presencia del dongle basta, porque un
dongle desenchufado no se enumera. Con dos internos o dos dongles, el factor no
distingue nada. Se muestra en la ventana, bajo el estado de cada radio, y ahí termina.

Lo que sí usa el estado del sistema es el orden inicial: al agregar un radio al grupo,
uno habilitado entra por encima de los que estén deshabilitados. Un radio que el
usuario ya había deshabilitado por su cuenta es una declaración de preferencia. Solo
decide el orden inicial; después manda lo que el usuario ordene a mano.

## Elevación

Habilitar y deshabilitar nodos PnP exige privilegios de administrador. El residente
igualmente corre sin elevar, para poder arrancar desde la clave `Run` y aparecer en
Configuración → Aplicaciones → Inicio con su propio interruptor.

El trabajo elevado lo hacen dos tareas programadas con `RunLevel HighestAvailable`,
`RightBTRadio.Apply` y `RightBTRadio.EnableAll`, que el residente dispara con
`schtasks /Run`. Las registra el instalador; si faltan, la aplicación las pide una vez.
Son por usuario, así que un segundo usuario de la misma máquina las registra desde la
ventana de ajustes.

Son dos tareas y no una con argumentos porque `schtasks /Run` no permite pasar
argumentos a la acción.

## Estructura

| Proyecto | Qué contiene |
|---|---|
| `RightBTRadio.Core` | Enumeración de radios, control de nodos PnP, configuración, resolución de prioridad, tareas programadas y contrato visual. Sin interfaz. |
| `RightBTRadio` | Residente del área de notificación (WinForms) y los modos sin interfaz. Corre sin elevar. |
| `RightBTRadio.WinUI` | Ventana de ajustes en WinUI 3, con Mica. Proceso aparte, bajo demanda. |
| `RightBTRadio.Tests` | Pruebas de la resolución de prioridad, del filtro de enumeradores y de la lectura de tareas. |

El residente nunca escribe la configuración: solo la lee. La ventana de ajustes es la
única que escribe. Por eso no hace falta el IPC que sí necesita RightKeyboard.

## Compilar y probar

```
dotnet build RightBTRadio.slnx
dotnet test RightBTRadio.slnx
```

Compile siempre la solución. Compilar un proyecto suelto escribe en otra carpeta de
salida, y entonces el residente y el arnés pueden medir un binario viejo.

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

Compila la solución, levanta la ventana de ajustes, afirma lo que promete
`RightBTRadio.Core/SettingsVisualContract.cs`, deja una captura en
`artifacts/ui-harness/` y cierra lo que abrió. Sale 0 si todo se afirmó, 1 si algo
falló.

| Parámetro | Para qué |
|---|---|
| `-Configuration` | `Debug` por omisión; `Release` para revisar lo que se publica |
| `-Attach` | Mide una ventana ya abierta en lugar de lanzarla, y no la cierra |
| `-KeepOpen` | Deja la aplicación viva al terminar |
| `-SkipEvidence` | Omite la captura |
| `-EvidenceDirectory` | Cambia dónde se deja la captura |

Necesita una sesión interactiva de Windows: `winapp ui` opera sobre UI Automation, y
hace falta el [CLI winapp](https://github.com/microsoft/winappcli).

Los textos esperados se leen del contrato en cada corrida, no se copian al script, así
que un cambio de contrato sin cambio de interfaz —o al revés— rompe el arnés a
propósito. Comprobado: al desviar el subtítulo de la ventana respecto del contrato, el
arnés falla esa aserción y sale 1.

El arnés usa solo los verbos que no inyectan entrada. La captura no es una aserción
sino evidencia; es lo primero que se puede omitir.

Lo que no resuelve: si Mica se está viendo de verdad —Windows cae a color sólido sin
avisar a la aplicación, y la captura sirve para desempatarlo a ojo—, la alineación y
los estados visuales, y la conexión o desconexión física de un radio.

## Construir el instalador

```
pwsh -File scripts/build-installer.ps1
```

Publica los dos ejecutables en una sola carpeta, dependientes del marco, descarta los
40 MB de Windows ML que el Windows App SDK arrastra sin que la aplicación los use, y
compila el instalador con Inno Setup. Deja el resultado y su SHA-256 en
`artifacts/installer/`.

`-SkipInstaller` publica y verifica sin llegar a Inno Setup.

El icono se genera por código y no se versiona como binario opaco:

```
pwsh -File scripts/make-icon.ps1
```

## Procedencia y licencia

MIT, archivo `LICENSE`.

El esqueleto viene de [RightKeyboard](https://github.com/n-a-monterocarvajal/RightKeyboard),
que es una obra derivada por capas: la anterior al fork está bajo CPOL 1.02 y los forks
intermedios no declararon licencia; solo los cambios propios del fork son MIT.

RightBTRadio reutiliza únicamente archivos de esa capa MIT, escritos en junio y julio
de 2026: `NativeTrayMenu.cs`, `StartupManager.cs`, `DeviceIdentityResolver.cs`,
`RawInputWindow.cs` y `TrayApplicationContext.cs`, más los criterios de
`SettingsPanelVisualContract.cs`, del arnés de interfaz y del instalador.

No se copió nada de la capa anterior al fork. Los dos archivos de RightKeyboard que
proceden de ella —`Program.cs`, importado el 7 de enero de 2020, y `Configuration.cs`,
del 13 de junio de 2020— se reescribieron aquí desde cero. El `Program.cs` de este
repositorio solo comparte con aquel el `[STAThread] static void Main` obligatorio, y su
`Configuration.cs` no comparte modelo ni código. El icono tampoco se pudo reutilizar:
el de RightKeyboard entró con esa misma importación de 2020.

La detección y descarga de prerequisitos del instalador viene de Collatio-Sen, del
mismo autor.
