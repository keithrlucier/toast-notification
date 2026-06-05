using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace ToastRevival.Agent;

/// <summary>
/// Shared network helpers used by both the desktop overlay and the agent hub client.
/// </summary>
internal static class NetworkUtils
{
    /// <summary>
    /// Returns the first UP, non-loopback, non-link-local IPv4 address, or null if none qualify.
    /// </summary>
    public static string? GetLocalIPv4()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var ip = ua.Address;
                    if (IPAddress.IsLoopback(ip)) continue;
                    var b = ip.GetAddressBytes();
                    if (b[0] == 169 && b[1] == 254) continue;
                    return ip.ToString();
                }
            }
        }
        catch (Exception ex)
        {
            DiagLog.Write($"NetworkUtils.GetLocalIPv4: {ex.GetType().Name}: {ex.Message}");
        }
        return null;
    }
}
