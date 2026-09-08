namespace RightBTRadio;

/// <summary>
/// Las operaciones que exigen elevación, sin interfaz. Las ejecutan los modos
/// <c>--apply</c> y <c>--enable-all</c>, que Windows lanza elevados desde las tareas
/// programadas de <see cref="ElevatedTasks"/>.
/// </summary>
internal static class RadioService
{
    // Perillas de calibración: el árbol PnP no cambia de estado al instante y el tiempo
    // que tarda depende del hardware. Medido en la estación de desarrollo, el radio
    // externo se recupera dentro del primer intento.
    private const int SettlingMilliseconds = 3000;
    private const int RecoveryAttempts = 6;


    /// <summary>
    /// Deja habilitado el radio presente de mayor prioridad del grupo y deshabilita el
    /// resto. Devuelve cuántos nodos cambiaron.
    /// </summary>
    public static int Apply(Configuration configuration)
    {
        IReadOnlyList<BluetoothRadio> present = BluetoothRadios.Enumerate();
        ResolutionPlan plan = PriorityResolver.Resolve(configuration.DefaultGroup, present);
        if (plan.IsEmpty)
        {
            return 0;
        }

        int changed = 0;

        // Habilitar antes de deshabilitar: así no hay un instante sin ningún radio.
        if (plan.Enable is not null && SetEnabled(plan.Enable, enabled: true))
        {
            changed++;
        }

        foreach (BluetoothRadio radio in plan.Disable)
        {
            if (SetEnabled(radio, enabled: false))
            {
                changed++;
            }
        }

        if (changed > 0 && plan.Winner is not null)
        {
            RecoverWinner(plan.Winner.HardwareId);
        }

        return changed;
    }

    /// <summary>Habilita todos los radios deshabilitados. Devuelve cuántos cambiaron.</summary>
    public static int EnableAll()
    {
        int changed = 0;
        foreach (BluetoothRadio radio in BluetoothRadios.Enumerate().Where(radio => !radio.Enabled))
        {
            if (SetEnabled(radio, enabled: true))
            {
                changed++;
            }
        }

        return changed;
    }

    /// <summary>
    /// Windows no admite dos radios Bluetooth a la vez: mientras había otro presente, el
    /// ganador queda con <c>CM_PROB_FAILED_INSTALL</c>, y el nodo no se recupera solo
    /// cuando el conflicto desaparece. Un ciclo de deshabilitar y volver a habilitar
    /// fuerza a Windows a cargar el controlador otra vez.
    /// </summary>
    /// <remarks>
    /// Medido sobre hardware real: con el radio en conflicto ya deshabilitado,
    /// <c>CM_Reenumerate_DevNode</c> con <c>CM_REENUMERATE_RETRY_INSTALLATION</c> deja el
    /// problema 31 intacto; el ciclo lo lleva a 0. Por eso no se reenumera.
    /// </remarks>
    // ponytail: el ciclo es lo que se comprobó que funciona, no lo que se demostró que
    // es la única vía. Quedan sin probar CM_Setup_DevNode con CM_SETUP_DEVNODE_RESET,
    // la reinstalación por SetupAPI (DIF_PROPERTYCHANGE con DICS_PROPCHANGE), y
    // reenumerar el nodo padre o la raíz en lugar del propio nodo. Investigar antes de
    // dar el ciclo por definitivo: deshabilitar y habilitar deja al equipo sin ningún
    // radio durante unos segundos, y una vía que no lo haga sería preferible.
    private static void RecoverWinner(string hardwareId)
    {
        // Vuelve a enumerar: el estado leído antes de los cambios ya no vale.
        BluetoothRadio? winner = Find(hardwareId);
        if (winner is null || !winner.HasProblem)
        {
            return;
        }

        Log.Write($"{winner.Name} quedó con el problema {winner.Problem}; se reinicia su nodo.");
        if (!DeviceControl.SetEnabled(winner.InstanceId, enabled: false))
        {
            return;
        }

        Thread.Sleep(SettlingMilliseconds);
        if (!DeviceControl.SetEnabled(winner.InstanceId, enabled: true))
        {
            // Peor caso: el ganador queda deshabilitado. El próximo evento de cambio de
            // dispositivos vuelve a resolver y lo habilita.
            Log.Write($"{winner.Name} quedó deshabilitado tras el reinicio del nodo.");
            return;
        }

        Log.Write($"Tras reiniciar el nodo, {winner.Name} informa el problema {WaitForRecovery(hardwareId)}.");
    }

    /// <summary>
    /// Espera a que Windows termine de instalar el controlador. Devuelve el código de
    /// problema final, 0 si se recuperó.
    /// </summary>
    private static string WaitForRecovery(string hardwareId)
    {
        for (int attempt = 0; attempt < RecoveryAttempts; attempt++)
        {
            Thread.Sleep(SettlingMilliseconds);
            BluetoothRadio? radio = Find(hardwareId);
            if (radio is null)
            {
                return "ausente";
            }

            if (!radio.HasProblem)
            {
                return radio.Problem.ToString();
            }
        }

        return "sin recuperar";
    }

    private static BluetoothRadio? Find(string hardwareId) => BluetoothRadios.Enumerate().FirstOrDefault(
        radio => string.Equals(radio.HardwareId, hardwareId, StringComparison.OrdinalIgnoreCase));

    private static bool SetEnabled(BluetoothRadio radio, bool enabled)
    {
        // Un fallo (permisos, dispositivo ocupado) queda registrado y se reintenta en el
        // próximo evento de cambio de dispositivos.
        if (!DeviceControl.SetEnabled(radio.InstanceId, enabled))
        {
            return false;
        }

        Log.Write($"{(enabled ? "Habilitado" : "Deshabilitado")} {radio.Name} ({radio.HardwareId}).");
        return true;
    }
}
