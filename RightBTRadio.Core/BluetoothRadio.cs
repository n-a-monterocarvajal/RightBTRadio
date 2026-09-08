namespace RightBTRadio;

/// <summary>
/// Un radio Bluetooth presente en el sistema, tal como lo describe SetupAPI.
/// </summary>
/// <param name="HardwareId">
/// Identificador estable con el que se empareja la configuración. Es el hardware ID
/// más específico del nodo (por ejemplo <c>USB\VID_368B&amp;PID_8D81&amp;REV_0100</c>),
/// no el instance ID, para que el radio siga reconociéndose al cambiarlo de puerto.
/// </param>
/// <param name="InstanceId">Identificador de la instancia concreta. Solo diagnóstico.</param>
/// <param name="Name">Nombre que muestra Windows.</param>
/// <param name="Enabled">Falso si el nodo está deshabilitado (CM_PROB_DISABLED).</param>
internal sealed record BluetoothRadio(
    string HardwareId,
    string InstanceId,
    string Name,
    bool Enabled);
