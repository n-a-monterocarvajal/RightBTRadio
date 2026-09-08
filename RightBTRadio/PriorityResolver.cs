namespace RightBTRadio;

/// <summary>
/// Cambios necesarios para que solo quede habilitado el radio de mayor prioridad.
/// Vacío cuando el estado ya es el correcto.
/// </summary>
internal sealed record ResolutionPlan(BluetoothRadio? Enable, IReadOnlyList<BluetoothRadio> Disable)
{
    public static readonly ResolutionPlan Empty = new(null, []);

    public bool IsEmpty => Enable is null && Disable.Count == 0;
}

internal static class PriorityResolver
{
    /// <summary>
    /// De los dispositivos del grupo que están presentes, deja habilitado el de mayor
    /// prioridad y deshabilita los demás. Los radios presentes que no pertenecen al
    /// grupo no se tocan: el usuario no pidió nada sobre ellos.
    /// </summary>
    public static ResolutionPlan Resolve(PriorityGroup group, IReadOnlyList<BluetoothRadio> present)
    {
        // Dos radios idénticos comparten hardware ID; se elige uno de forma
        // determinista para que la resolución no oscile entre ejecuciones.
        Dictionary<string, BluetoothRadio> byHardwareId = present
            .GroupBy(radio => radio.HardwareId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                candidates => candidates.Key,
                candidates => candidates.OrderBy(radio => radio.InstanceId, StringComparer.OrdinalIgnoreCase).First(),
                StringComparer.OrdinalIgnoreCase);

        List<BluetoothRadio> candidates = [];
        foreach (ConfiguredDevice device in group.Devices)
        {
            if (byHardwareId.TryGetValue(device.HardwareId, out BluetoothRadio? radio))
            {
                candidates.Add(radio);
            }
        }

        if (candidates.Count == 0)
        {
            return ResolutionPlan.Empty;
        }

        BluetoothRadio winner = candidates[0];
        return new ResolutionPlan(
            winner.Enabled ? null : winner,
            candidates.Skip(1).Where(radio => radio.Enabled).ToList());
    }
}
