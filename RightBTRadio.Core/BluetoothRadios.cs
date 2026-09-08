using System.Runtime.InteropServices;
using System.Text;

namespace RightBTRadio;

/// <summary>
/// Enumera los radios Bluetooth presentes mediante SetupAPI. El interop reproduce
/// el de <c>DeviceIdentityResolver</c> de RightKeyboard, con dos cambios: recorre una
/// clase de instalación en vez de una clase de interfaz, y consulta el estado del
/// nodo con cfgmgr32 para saber si está deshabilitado.
/// </summary>
internal static class BluetoothRadios
{
    private const uint DigcfPresent = 0x00000002;
    private const uint SpdrpDeviceDesc = 0x00000000;
    private const uint SpdrpHardwareId = 0x00000001;
    private const uint SpdrpFriendlyName = 0x0000000C;
    private const uint DnHasProblem = 0x00000400;
    private static readonly nint InvalidHandleValue = new(-1);

    /// <summary>Clase de instalación Bluetooth.</summary>
    private static readonly Guid BluetoothClass = new("e0cbf06c-cd8b-4647-bb8a-263b43f0f974");

    // Los radios los enumera el bus (USB\, PCI\, ...); todo lo que cuelga de un radio
    // —dispositivos emparejados, servicios y los enumeradores de Microsoft— lo enumera
    // BTH. Filtrar por enumerador separa unos de otros sin listas de nombres.
    // ponytail: heurística por prefijo de enumerador. Si algún día aparece un radio
    // con enumerador BTH, cambiar a comprobar que el padre no sea de clase Bluetooth.
    private static readonly string[] ChildEnumerators = ["BTH\\", "BTHENUM\\", "BTHLE\\", "BTHLEDEVICE\\"];

    /// <summary>
    /// Devuelve los radios presentes, incluidos los deshabilitados: siguen en el árbol
    /// de dispositivos y hay que poder volver a habilitarlos.
    /// </summary>
    public static IReadOnlyList<BluetoothRadio> Enumerate()
    {
        List<BluetoothRadio> radios = [];
        Guid setupClass = BluetoothClass;
        nint deviceSet = SetupDiGetClassDevsW(ref setupClass, null, 0, DigcfPresent);
        if (deviceSet == InvalidHandleValue)
        {
            return radios;
        }

        try
        {
            DeviceInfoData deviceInfo = new() { Size = (uint)Marshal.SizeOf<DeviceInfoData>() };
            for (uint index = 0; SetupDiEnumDeviceInfo(deviceSet, index, ref deviceInfo); index++)
            {
                string? instanceId = ReadInstanceId(deviceSet, ref deviceInfo);
                if (instanceId is null || IsChildOfARadio(instanceId))
                {
                    continue;
                }

                IReadOnlyList<string> hardwareIds = ReadRegistryStrings(deviceSet, ref deviceInfo, SpdrpHardwareId);
                if (hardwareIds.Count == 0)
                {
                    continue;
                }

                string name = ReadRegistryStrings(deviceSet, ref deviceInfo, SpdrpFriendlyName).FirstOrDefault()
                    ?? ReadRegistryStrings(deviceSet, ref deviceInfo, SpdrpDeviceDesc).FirstOrDefault()
                    ?? instanceId;

                radios.Add(new BluetoothRadio(
                    hardwareIds[0],
                    instanceId,
                    name,
                    ReadProblem(deviceInfo.DeviceInstance)));

                deviceInfo = new DeviceInfoData { Size = (uint)Marshal.SizeOf<DeviceInfoData>() };
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceSet);
        }

        return radios;
    }

    internal static bool IsChildOfARadio(string instanceId) =>
        ChildEnumerators.Any(prefix => instanceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static uint ReadProblem(uint deviceInstance)
    {
        if (CM_Get_DevNode_Status(out uint status, out uint problem, deviceInstance, 0) != 0)
        {
            // Sin estado fiable, tratarlo como sano: la resolución lo intentará cambiar
            // si toca y el fallo se registra ahí.
            return 0;
        }

        return (status & DnHasProblem) == 0 ? 0 : problem;
    }

    private static string? ReadInstanceId(nint deviceSet, ref DeviceInfoData deviceInfo)
    {
        SetupDiGetDeviceInstanceIdW(deviceSet, ref deviceInfo, null, 0, out uint size);
        if (size == 0)
        {
            return null;
        }

        StringBuilder value = new(checked((int)size));
        return SetupDiGetDeviceInstanceIdW(deviceSet, ref deviceInfo, value, size, out _)
            ? value.ToString()
            : null;
    }

    private static IReadOnlyList<string> ReadRegistryStrings(nint deviceSet, ref DeviceInfoData deviceInfo, uint property)
    {
        SetupDiGetDeviceRegistryPropertyW(deviceSet, ref deviceInfo, property, out _, null, 0, out uint size);
        if (size < sizeof(char))
        {
            return [];
        }

        byte[] buffer = new byte[size];
        if (!SetupDiGetDeviceRegistryPropertyW(deviceSet, ref deviceInfo, property, out _, buffer, size, out _))
        {
            return [];
        }

        return Encoding.Unicode.GetString(buffer)
            .Split('\0', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DeviceInfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DeviceInstance;
        public nint Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint SetupDiGetClassDevsW(
        ref Guid classGuid,
        string? enumerator,
        nint parentWindow,
        uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(nint deviceInfoSet, uint memberIndex, ref DeviceInfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(
        nint deviceInfoSet,
        ref DeviceInfoData deviceInfoData,
        uint property,
        out uint propertyType,
        [Out] byte[]? propertyBuffer,
        uint propertyBufferSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceIdW(
        nint deviceInfoSet,
        ref DeviceInfoData deviceInfoData,
        StringBuilder? deviceInstanceId,
        uint deviceInstanceIdSize,
        out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint deviceInstance, uint flags);
}
