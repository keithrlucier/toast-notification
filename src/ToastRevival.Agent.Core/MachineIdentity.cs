namespace ToastRevival.Agent.Core;

/// <summary>
/// MachineGuid device-identity milestone (COLLECTOR phase). Pure normalization of the
/// two machine-identity signals the agent and the health service read from the registry:
///   - MachineGuid : HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid
///   - DnsHostName : HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Hostname
///                   (the FULL, non-truncated primary hostname — not the 15-char NetBIOS
///                   name that Environment.MachineName returns).
///
/// The actual Registry.GetValue calls live in the Windows projects (the agent and the
/// LocalSystem health service); only the parse/normalize rules live here so they are
/// unit-tested once and stay identical on both report paths — the same split that keeps
/// <see cref="BootstrapEnv.TryParse"/> testable.
///
/// COLLECTOR PHASE CONTRACT: these values are REPORTED by the agent/service and STORED by
/// the server only. Device matching is unchanged (still keyed on DeviceName), so nothing
/// derived from these can merge a device row or move a license seat. They are being
/// gathered to measure MachineGuid uniqueness across the fleet (the factory-clone
/// collision risk) and the true hostname BEFORE any merge is designed.
/// </summary>
public static class MachineIdentity
{
    /// <summary>
    /// Canonicalizes a raw MachineGuid string to lowercase 8-4-4-4-12 form ("d" format,
    /// no braces), or null when the value is missing, blank, all-zero, or not a GUID.
    /// The registry stores it brace-less already; normalizing makes the eventual
    /// server-side equality match robust to a braces/upper-case variant and rejects the
    /// degenerate empty GUID outright.
    /// </summary>
    public static string? NormalizeMachineGuid(string? raw)
        => Guid.TryParse(raw, out var g) && g != Guid.Empty
            ? g.ToString("d")
            : null;

    /// <summary>
    /// Trims a raw hostname to the value to store, or null when missing/blank. Capped at
    /// 256 chars to match the DnsHostName column bound (a real DNS hostname is &lt;= 253).
    /// No case-folding: a hostname is displayed as the OS reports it.
    /// </summary>
    public static string? NormalizeHostName(string? raw)
    {
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        return trimmed.Length > 256 ? trimmed[..256] : trimmed;
    }
}
