namespace RightBTRadio;

/// <summary>
/// Un radio Bluetooth presente en el sistema, tal como lo describe SetupAPI.
/// </summary>
/// <param name="HardwareId">
/// Identificador estable con el que se empareja la configuración. Es el hardware ID
/// más específico del nodo (por ejemplo <c>USB\VID_368B&amp;PID_8D81&amp;REV_0100</c>),
/// no el instance ID, para que el radio siga reconociéndose al cambiarlo de puerto.
/// </param>
/// <param name="InstanceId">Identificador de la instancia concreta. Es lo que recibe cfgmgr32.</param>
/// <param name="Name">Nombre que muestra Windows.</param>
/// <param name="Problem">
/// Código de problema del nodo, 0 si no tiene ninguno. Interesan dos: 22
/// (<c>CM_PROB_DISABLED</c>), que es el que pone esta aplicación, y 31
/// (<c>CM_PROB_FAILED_INSTALL</c>), que es como se manifiesta el conflicto entre dos
/// radios Bluetooth presentes a la vez.
/// </param>
internal sealed record BluetoothRadio(
    string HardwareId,
    string InstanceId,
    string Name,
    uint Problem)
{
    public const uint ProblemDisabled = 22;

    /// <summary>Falso solo si el nodo está deshabilitado; otros problemas no lo son.</summary>
    public bool Enabled => Problem != ProblemDisabled;

    /// <summary>Verdadero si el nodo está habilitado pero Windows no lo pudo poner en marcha.</summary>
    public bool HasProblem => Problem != 0 && Problem != ProblemDisabled;
}
