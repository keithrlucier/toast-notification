using System.Text.Json;
using System.Text.Json.Serialization;

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
    [property: JsonPropertyName("signingKey")] string  SigningKey);

/// <summary>
/// Bootstrap config dropped next to the exe by the MSI/MSIX installer (D9).
/// Contains the values needed to register: tenant + server URL.
/// </summary>
internal sealed record BootstrapConfig(
    [property: JsonPropertyName("tenantId")]  Guid   TenantId,
    [property: JsonPropertyName("serverUrl")] string ServerUrl);

internal static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
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
            var json = File.ReadAllText(path);
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
        var json = JsonSerializer.Serialize(config, JsonOptions);

        // Write to a temp file then move atomically to prevent half-written config
        // surviving a crash mid-write.
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    public static BootstrapConfig? TryLoadBootstrap()
    {
        // Walk a few candidate locations. The MSI/MSIX installer (D9) will write
        // bootstrap.json next to the exe at install time using MSI properties
        // CLIENTID + SERVERURL. For dev/diagnostic launches, env vars override.
        var envTenant = Environment.GetEnvironmentVariable("TOAST_TENANT_ID");
        var envServer = Environment.GetEnvironmentVariable("TOAST_SERVER_URL");
        if (Guid.TryParse(envTenant, out var envTenantId) && !string.IsNullOrWhiteSpace(envServer))
        {
            return new BootstrapConfig(envTenantId, envServer);
        }

        var beside = Path.Combine(AppContext.BaseDirectory, "bootstrap.json");
        if (File.Exists(beside))
        {
            try
            {
                var json = File.ReadAllText(beside);
                return JsonSerializer.Deserialize<BootstrapConfig>(json);
            }
            catch (Exception ex)
            {
                DiagLog.Write($"ConfigStore.TryLoadBootstrap failed at '{beside}': {ex.GetType().Name}: {ex.Message}");
            }
        }

        return null;
    }
}
