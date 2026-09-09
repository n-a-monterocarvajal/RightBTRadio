# Registro de cambios

El formato sigue [Keep a Changelog](https://keepachangelog.com/es-ES/1.1.0/) y el
versionado sigue [SemVer](https://semver.org/lang/es/).

## [0.1.0-alpha] — 2026-09-09

Primera versión publicable. Alpha: la lógica está probada sobre hardware real, pero
sobre una sola estación y un solo par de radios.

### Agregado

- Resolución de prioridad entre radios Bluetooth. De los radios del grupo que están
  presentes, deja habilitado el de mayor prioridad y deshabilita el resto. La prioridad
  es la posición en la lista, no un número aparte, así que reordenar y renumerar no
  pueden divergir.
- Recuperación del radio ganador. Windows no maneja dos radios Bluetooth a la vez y
  deja a uno con el código de problema 31; deshabilitar al perdedor no basta porque el
  nodo del ganador no se recupera solo. Se reinicia su nodo, con una espera mínima
  entre intentos para no dejarlo encendiéndose y apagándose en bucle.
- Detección por eventos, sin temporizador: el residente escucha `WM_DEVICECHANGE` y
  resuelve al conectar o desconectar un radio.
- Ventana de ajustes en WinUI 3 con Mica, en una sola página. Muestra por cada radio si
  está conectado, si es interno o externo, su estado y la descripción del estado del
  dispositivo. Se refresca sola mientras está abierta.
- Área de notificación con «Habilitar todos los radios» como salida de emergencia, por
  si el radio deshabilitado era el del teclado o el ratón.
- Arranque con Windows por la clave `Run`, con su interruptor nativo en
  Configuración → Aplicaciones → Inicio.
- Modos sin interfaz `--apply`, `--enable-all`, `--list`, `--register-tasks` y
  `--unregister-tasks`.
- Instalador con descarga de prerequisitos. 7,7 MB; descarga el .NET Desktop Runtime y
  el Windows App Runtime solo si faltan. Registra las tareas programadas durante la
  instalación, así que la aplicación no vuelve a pedir permisos. La desinstalación
  devuelve todos los radios antes de borrar nada.
- Arnés de interfaz sobre el CLI winapp, con 17 aserciones leídas del contrato visual.

### Limitaciones conocidas

- Probado sobre una sola estación, con un radio interno Qualcomm Atheros AR3012 y un
  adaptador USB Toocki.
- Las tareas programadas son por usuario. Un segundo usuario de la misma máquina las
  registra desde la ventana de ajustes, con un aviso de permisos.
- Windows retrasa las entradas de la clave `Run` un par de minutos tras iniciar sesión.
  En ese intervalo la aplicación todavía no resuelve; el estado anterior persiste, así
  que solo importa si se conecta un radio justo entonces.
- El modelo admite varios grupos de prioridad, pero la ventana solo expone uno.
- Queda por comprobar si reiniciar el nodo es la única forma de recuperar un radio con
  el problema 31. Anotado en el código.
