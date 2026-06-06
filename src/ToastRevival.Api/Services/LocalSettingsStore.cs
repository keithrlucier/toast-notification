using System.Text.Json;
using System.Text.Json.Nodes;

namespace ToastRevival.Api.Services;

/// <summary>
/// ARCH-L1 — shared file-backed JSON settings store for platform admin config services.
/// Centralises the ReadOrCreateRoot/WriteRoot pattern that was copy-pasted across
/// BillingConfigService, MessagingConfigService, and SsoConfigService, and eliminates
/// the three separate static FileLock objects they each declared (concurrent write race).
/// All callers share the single FileLock so writes to the same appsettings.Local.json
/// are serialised regardless of which config service initiates them.
/// </summary>
public static class LocalSettingsStore
{
    // Single lock guards all writes to appsettings.Local.json, regardless of which
    // config service is calling. Three separate locks (the previous pattern) allowed
    // BillingConfigService and MessagingConfigService to write simultaneously and
    // corrupt the file.
    public static readonly object FileLock = new();

    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// Reads the JSON object at <paramref name="path"/>, returning an empty object if
    /// the file does not exist or is blank. Throws if the file contains non-object JSON.
    /// Must be called inside a <c>lock (LocalSettingsStore.FileLock)</c> block.
    /// </summary>
    public static JsonObject ReadOrCreateRoot(string path)
    {
        if (!File.Exists(path)) return new JsonObject();
        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text)) return new JsonObject();
        return JsonNode.Parse(text) as JsonObject
            ?? throw new InvalidOperationException("appsettings.Local.json must be a JSON object.");
    }

    /// <summary>
    /// Serialises <paramref name="root"/> to <paramref name="path"/> with a trailing newline.
    /// Must be called inside a <c>lock (LocalSettingsStore.FileLock)</c> block.
    /// </summary>
    public static void WriteRoot(string path, JsonObject root)
    {
        File.WriteAllText(path, root.ToJsonString(JsonOptions) + Environment.NewLine);
    }
}
