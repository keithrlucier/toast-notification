using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;
using ToastRevival.Agent.Core;

namespace ToastRevival.Agent;

/// <summary>
/// Persisted per-device state. Created on first-run registration, loaded on
/// every subsequent launch. Lives at %LOCALAPPDATA%\Toast2IT\Toast Notification\config.json
/// (or the package's LocalState equivalent when running packaged).
/// </summary>
internal sealed record DeviceConfig(
    [property: JsonPropertyName("tenantId")]   Guid    TenantId,
    [property: JsonPropertyName("serverUrl")]  string  ServerUrl,
    [property: JsonPropertyName("deviceId")]   Guid    DeviceId,
    [property: JsonPropertyName("deviceToken")]string  DeviceToken,
    [property: JsonPropertyName("signingKey")] string  SigningKey,
    [property: JsonPropertyName("tenantName")] string? TenantName = null);

/// <summary>
/// Bootstrap config dropped next to the exe by the MSI/MSIX installer (D9).
/// Contains the values needed to register: tenant, server URL, and optional
/// enrollment key (required when the tenant has EnrollmentKey gating enabled).
/// </summary>
internal sealed record BootstrapConfig(
    [property: JsonPropertyName("tenantId")]     Guid   TenantId,
    [property: JsonPropertyName("serverUrl")]    string ServerUrl,
    [property: JsonPropertyName("enrollmentKey")] string? EnrollmentKey = null);

internal static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
    };

    public static string GetConfigDirectory()
    {
        // Match DiagLog: prefer the package's LocalFolder when packaged so all
        // per-device state lives in the package container. Fall back to a stable
        // %LOCALAPPDATA% path for the unpackaged MSI/dev-run case.
        try
        {
            return Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        }
        catch
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Toast2IT", "Toast Notification");
        }
    }

    public static string GetConfigPath() =>
        Path.Combine(GetConfigDirectory(), "config.json");

    public static DeviceConfig? TryLoad()
    {
        var path = GetConfigPath();
        if (!File.Exists(path)) return null;

        try
        {
            var raw  = File.ReadAllBytes(path);
            var json = Unprotect(raw);
            return JsonSerializer.Deserialize<DeviceConfig>(json);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"ConfigStore.TryLoad failed at '{path}': {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public static void Save(DeviceConfig config)
    {
        var dir = GetConfigDirectory();
        Directory.CreateDirectory(dir);

        var path = Path.Combine(dir, "config.json");
        var json = JsonSerializer.SerializeToUtf8Bytes(config, JsonOptions);
        var cipherBytes = Protect(json);

        // Write to a temp file then move atomically to prevent half-written
        // config surviving a crash mid-write. DPAPI CurrentUser scope means
        // only the OS user account that wrote the config can read it back.
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, cipherBytes);
        File.Move(temp, path, overwrite: true);
    }

    // REVIEW-2026-06-06 WSEC-M3 REJECTED-by-design: DPAPI CurrentUser scope is correct for per-user agent installation; MachineKey scope would expose the HMAC signing key to all users on the machine, which is a worse threat model in shared-workstation MSP environments
    // DPAPI CurrentUser scope: only the OS user account that wrote the config can
    // read it. An attacker with admin credentials on the endpoint can still read
    // SYSTEM-scope data, but not per-user CurrentUser-scope data without the user's
    // credentials. Entropy null uses the machine+user key material only.
    private static byte[] Protect(byte[] plaintext) =>
        ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser);

    private static string Unprotect(byte[] cipherBytes)
    {
        var plaintext = ProtectedData.Unprotect(cipherBytes, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(plaintext);
    }

    private const string BootstrapRegistryKey = @"SOFTWARE\Toast2IT\Toast Notification";

    // Case-insensitive so a bootstrap.json written by ANY source (the MSI's own
    // writer, an RMM script, a hand edit) registers regardless of key casing.
    // The agent deserialized case-sensitively before 0.4.33, so a PascalCase
    // fallback file silently produced an empty TenantId and the device never
    // checked in. This makes the casing irrelevant.
    private static readonly JsonSerializerOptions BootstrapJsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public static BootstrapConfig? TryLoadBootstrap()
    {
        // Resolution order, most-trusted / most-reliable first:
        //   1. Env vars            — dev / diagnostic override.
        //   2. HKLM registry       — written NATIVELY by the MSI (no exe run), so
        //                            AV/EDR can't block it the way it blocks the
        //                            WriteBootstrapJson custom action (MSI 1721).
        //                            This is the reliable path on hardened fleets.
        //   3. bootstrap.json      — legacy / Velopack / manual placement.
        var fromEnv = BootstrapEnv.TryParse(
            Environment.GetEnvironmentVariable("TOAST_TENANT_ID"),
            Environment.GetEnvironmentVariable("TOAST_SERVER_URL"));
        if (fromEnv is not null)
            return new BootstrapConfig(fromEnv.Value.TenantId, fromEnv.Value.ServerUrl);

        var fromRegistry = TryLoadBootstrapFromRegistry();
        if (fromRegistry is not null) return fromRegistry;

        var beside = Path.Combine(AppContext.BaseDirectory, "bootstrap.json");
        if (File.Exists(beside))
        {
            try
            {
                var json = File.ReadAllText(beside);
                return JsonSerializer.Deserialize<BootstrapConfig>(json, BootstrapJsonOptions);
            }
            catch (Exception ex)
            {
                DiagLog.Write($"ConfigStore.TryLoadBootstrap failed at '{beside}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        return null;
    }

    /// <summary>
    /// Reads bootstrap config from HKLM\SOFTWARE\Toast2IT\Toast Notification —
    /// TenantId + ServerUrl (+ optional EnrollmentKey). The MSI writes these as
    /// native RegistryValue rows at install time from its CLIENTID/SERVERURL/
    /// ENROLLMENTKEY properties, so this path never depends on executing the
    /// agent binary during install (the AV-blocked WriteBootstrapJson failure
    /// mode). Returns null if the values aren't present or aren't valid.
    /// </summary>
    private static BootstrapConfig? TryLoadBootstrapFromRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(BootstrapRegistryKey);
            if (key is null) return null;

            var tenant = key.GetValue("TenantId") as string;
            var server = key.GetValue("ServerUrl") as string;
            if (!Guid.TryParse(tenant, out var tenantId) || string.IsNullOrWhiteSpace(server))
                return null;

            var enroll = key.GetValue("EnrollmentKey") as string;
            if (string.IsNullOrWhiteSpace(enroll)) enroll = null;

            DiagLog.Write("ConfigStore: bootstrap loaded from HKLM registry.");
            return new BootstrapConfig(tenantId, server, enroll);
        }
        catch (Exception ex)
        {
            DiagLog.Write($"ConfigStore.TryLoadBootstrapFromRegistry failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }
}
