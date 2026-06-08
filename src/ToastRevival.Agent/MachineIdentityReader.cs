using Microsoft.Win32;
using ToastRevival.Agent.Core;

namespace ToastRevival.Agent;

/// <summary>
/// Reads the two machine-identity signals from HKLM (explicit 64-bit view) and normalizes
/// them through the unit-tested <see cref="MachineIdentity"/> helpers:
///   MachineGuid : SOFTWARE\Microsoft\Cryptography\MachineGuid
///   DnsHostName : SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Hostname (full,
///                 non-truncated primary hostname — NOT Environment.MachineName's 15-char
///                 NetBIOS name).
/// Both reads are best-effort: any failure yields null so registration/heartbeat never
/// breaks on a locked-down box. Collector phase — reported + stored only; the server still
/// matches devices by DeviceName, so nothing here can merge a row or move a seat.
/// </summary>
internal static class MachineIdentityReader
{
    private const string CryptographyKey = @"SOFTWARE\Microsoft\Cryptography";
    private const string TcpipParamsKey  = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";

    public static (string? MachineGuid, string? DnsHostName) Read()
        => (ReadValue(CryptographyKey, "MachineGuid", MachineIdentity.NormalizeMachineGuid),
            ReadValue(TcpipParamsKey,  "Hostname",    MachineIdentity.NormalizeHostName));

    private static string? ReadValue(string subKey, string valueName, Func<string?, string?> normalize)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey);
            return normalize(key?.GetValue(valueName) as string);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"MachineIdentityReader: reading {subKey}\\{valueName} failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
