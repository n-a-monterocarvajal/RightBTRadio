namespace RightBTRadio;

/// <summary>
/// Qué radio del grupo debe quedar habilitado y qué cambios hacen falta para llegar ahí.
/// </summary>
/// <param name="Winner">
/// El radio presente de mayor prioridad, esté ya habilitado o no. Nulo si no hay ninguno
/// del grupo presente.
/// </param>
/// <param name="Enable">El ganador, solo si estaba deshabilitado.</param>
/// <param name="Disable">Los demás presentes del grupo que estén habilitados.</param>
internal sealed record ResolutionPlan(
    BluetoothRadio? Winner,
    BluetoothRadio? Enable,
    IReadOnlyList<BluetoothRadio> Disable)
{
    public static readonly ResolutionPlan Empty = new(null, null, []);
}

internal static class PriorityResolver
{
    /// <summary>
    /// De los dispositivos del grupo que están presentes, deja habilitado el de mayor
    /// prioridad y deshabilita los demás. Los radios presentes que no pertenecen al
    /// grupo no se tocan: el usuario no pidió nada sobre ellos.
    /// </summary>
    public static ResolutionPlan Resolve(IReadOnlyList<ConfiguredDevice> group, IReadOnlyList<BluetoothRadio> present)
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
        foreach (ConfiguredDevice device in group)
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
            winner,
            winner.Enabled ? null : winner,
            candidates.Skip(1).Where(radio => radio.Enabled).ToList());
    }
}
