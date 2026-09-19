# Registro de cambios

El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y las
versiones siguen [SemVer](https://semver.org/lang/es/).

## [0.1.0-alpha] - 2026-09-17

Primera versión publicada. Es una alpha: la lógica se probó con hardware real, pero en
un solo equipo y con un solo par de radios.

### Agregado

- Prioridad entre radios Bluetooth. De los radios del grupo que están conectados,
  queda habilitado el de mayor prioridad y se deshabilitan los demás. La prioridad es
  la posición en la lista y no un número aparte, así que el orden y la numeración
  siempre coinciden.
- Recuperación del radio ganador. Windows no maneja dos radios Bluetooth a la vez y
  deja uno con el código de problema 31. Deshabilitar el otro no alcanza, porque el
  nodo del ganador no se recupera solo. La aplicación reinicia ese nodo y espera un
  tiempo mínimo entre intentos para no encenderlo y apagarlo en bucle.
- Detección por eventos. El residente escucha `WM_DEVICECHANGE` y aplica la prioridad
  al conectar o desconectar un radio, sin consultar periódicamente.
- Ventana de ajustes en WinUI 3 con Mica. Muestra los radios detectados y el grupo de
  prioridad en dos paneles. Para cada radio indica si está conectado, si es interno o
  externo, su estado y la descripción que usa el Administrador de dispositivos. La
  lista se actualiza sola mientras la ventana está abierta.
- Botones en la barra de título para ajustes (inicio con Windows, inicio en el área de
  notificación y tema), información de la aplicación y ayuda con preguntas
  frecuentes.
- Icono propio con el símbolo de Bluetooth y una marca de verificación.
- Menú del área de notificación con «Habilitar todos los radios», para recuperar el
  teclado o el mouse si dependían del radio deshabilitado.
- Inicio con Windows mediante la clave `Run`, con su interruptor en
  Configuración → Aplicaciones → Inicio.
- Modos de línea de comandos `--apply`, `--enable-all`, `--list`, `--register-tasks` y
  `--unregister-tasks`.
- Instalador de 7,7 MB. Descarga el .NET Desktop Runtime y el Windows App Runtime solo
  si faltan, y registra las tareas programadas durante la instalación para que la
  aplicación no vuelva a pedir permisos. Al desinstalar, habilita todos los radios
  antes de borrar los archivos.
- Arnés de interfaz sobre el CLI winapp, con 21 comprobaciones que lee del contrato
  visual.

### Corregido

- La salida de `schtasks` se lee con la página de códigos OEM de la consola. Antes, una
  ruta con «ñ» impedía reconocer las tareas y la aplicación pedía permisos en cada
  inicio.
- El residente escucha la señal de cierre del instalador antes de mostrar el aviso de
  registro de tareas. Antes, con el aviso abierto, el instalador esperaba sin
  respuesta y cancelaba la instalación.

- Un segundo conflicto poco después de una recuperación exitosa se atiende enseguida.
  Antes, la espera entre intentos también frenaba ese caso y el adaptador quedaba con
  el código 31 durante dos minutos.
- Cada radio conserva su nombre en las dos listas, esté conectado o no.

### Limitaciones conocidas

- Se probó en un solo equipo, con un radio interno Qualcomm Atheros AR3012 y un
  adaptador USB Toocki.
- Las tareas programadas son por usuario. Otro usuario del mismo equipo las registra
  al abrir la aplicación, que le pide permiso de administrador.
- Windows retrasa un par de minutos las aplicaciones de la clave `Run` después de
  iniciar sesión. Durante ese tiempo la aplicación no aplica la prioridad. El estado
  anterior se mantiene, así que solo importa si se conecta un radio en ese intervalo.
- Falta comprobar si reiniciar el nodo es la única forma de recuperar un radio con el
  código 31.

[0.1.0-alpha]: https://github.com/n-a-monterocarvajal/RightBTRadio/releases/tag/v0.1.0-alpha
