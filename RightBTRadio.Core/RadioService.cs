namespace RightBTRadio;

/// <summary>
/// Las dos operaciones que exigen elevación, sin interfaz. Las ejecutan los modos
/// <c>--apply</c> y <c>--enable-all</c>, que Windows lanza elevados desde las tareas
/// programadas de <see cref="ElevatedTasks"/>.
/// </summary>
internal static class RadioService
{
    /// <summary>
    /// Deja habilitado el radio presente de mayor prioridad del grupo y deshabilita el
    /// resto. Devuelve cuántos nodos cambiaron.
    /// </summary>
    public static int Apply(Configuration configuration)
    {
        ResolutionPlan plan = PriorityResolver.Resolve(configuration.DefaultGroup, BluetoothRadios.Enumerate());
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
