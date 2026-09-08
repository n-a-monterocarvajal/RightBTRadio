using System.Runtime.InteropServices;

namespace RightBTRadio;

/// <summary>
/// Habilita y deshabilita nodos PnP con cfgmgr32, el equivalente directo de
/// <c>Enable-PnpDevice</c> / <c>Disable-PnpDevice</c> sin lanzar PowerShell.
/// Requiere privilegios de administrador.
/// </summary>
internal static class DeviceControl
{
    private const int CrSuccess = 0;
    private const uint CmLocateDevNodeNormal = 0;

    // Como devcon: persiste el cambio entre reinicios y prohíbe que Windows muestre
    // interfaz propia, que en un proceso de bandeja no tendría dónde aparecer.
    private const uint CmDisableUiNotOk = 0x00000004;
    private const uint CmDisablePersist = 0x00000008;

    /// <summary>Devuelve verdadero si el cambio se aplicó.</summary>
    public static bool SetEnabled(string instanceId, bool enabled)
    {
        int located = CM_Locate_DevNodeW(out uint deviceInstance, instanceId, CmLocateDevNodeNormal);
        if (located != CrSuccess)
        {
            Log.Write($"No se pudo localizar el nodo {instanceId} (CR {located}).");
            return false;
        }

        int result = enabled
            ? CM_Enable_DevNode(deviceInstance, 0)
            : CM_Disable_DevNode(deviceInstance, CmDisablePersist | CmDisableUiNotOk);

        if (result != CrSuccess)
        {
            string action = enabled ? "habilitar" : "deshabilitar";
            Log.Write($"No se pudo {action} {instanceId} (CR {result}).");
            return false;
        }

        return true;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint deviceInstance, string deviceInstanceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Enable_DevNode(uint deviceInstance, uint flags);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Disable_DevNode(uint deviceInstance, uint flags);
}
