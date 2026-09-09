namespace RightBTRadio;

/// <summary>
/// Describe el estado de un nodo con la misma idea que el campo «Estado del dispositivo»
/// del Administrador de dispositivos.
/// </summary>
/// <remarks>
/// El texto es propio y no el de Windows. Las cadenas del Administrador de dispositivos
/// están en la tabla de cadenas de <c>devmgr.dll</c>, y se comprobó que se pueden leer
/// con <c>LoadString</c>; lo que no se puede es calcular el identificador de recurso a
/// partir del código de problema. No hay un desplazamiento fijo: los códigos 20 y 23 no
/// tienen entrada y corren el resto del rango. Fijar los identificadores a mano ataría la
/// aplicación a un detalle interno de un componente de interfaz de Windows, que puede
/// cambiar en cualquier actualización. Solo se describen los códigos que este escenario
/// puede producir; el resto se informa por número.
/// </remarks>
internal static class DeviceStatusText
{
    public static string Describe(uint problem) => problem switch
    {
        0 => "Este dispositivo funciona correctamente.",
        1 => "El dispositivo no está configurado correctamente.",
        10 => "Este dispositivo no puede iniciar.",
        12 => "Este dispositivo no encuentra suficientes recursos libres para su uso.",
        14 => "El dispositivo no funcionará correctamente hasta que reinicie el equipo.",
        18 => "Vuelva a instalar los controladores para este dispositivo.",
        19 => "La información de configuración del registro está incompleta o dañada.",
        21 => "Windows está quitando este dispositivo.",
        22 => "Este dispositivo está deshabilitado.",
        24 => "Este dispositivo no está presente o no está funcionando correctamente.",
        28 => "No están instalados los controladores para este dispositivo.",
        31 => "Windows no puede cargar los controladores necesarios para este dispositivo.",
        43 => "Windows detuvo este dispositivo porque informó de un problema.",
        45 => "Este dispositivo no está conectado al equipo.",
        _ => $"Este dispositivo informa el problema {problem}."
    };
}
