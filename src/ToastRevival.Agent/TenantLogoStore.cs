using System.Net.Http.Headers;

namespace ToastRevival.Agent;

/// <summary>
/// Downloads the tenant-configured logo to a local file so the agent can use it
/// as the AUMID IconUri (the tiny attribution icon Windows shows at the top of
/// every toast next to the tenant name).
///
/// The HKCU AUMID IconUri value must be a local filesystem path — Windows does
/// not resolve http(s) URLs there. We therefore mirror the tenant.LogoUrl
/// payload from /api/devices/tenant-name to disk on every startup, then hand the
/// local path to <see cref="NotificationDisplayName"/>.
///
/// Storage lives next to config.json (per-user LocalAppData). Best-effort: any
/// failure returns null and the caller falls back to the bundled
/// Assets\toast-logo.png shipped with the agent.
/// </summary>
internal static class TenantLogoStore
{
    private const string FilePrefix = "tenant-logo";
    private const long MaxBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(15);

    // WSEC-M1: allowlist of Content-Type values we'll accept as logo images.
    private static readonly HashSet<string> AllowedMediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/jpg", "image/gif", "image/webp", "image/svg+xml"
    };

    /// <summary>
    /// Downloads <paramref name="logoUrl"/> to a stable local path and returns
    /// that path. Returns null on any failure (network, non-200, payload too
    /// large, IO error). If <paramref name="logoUrl"/> is null/whitespace, the
    /// method also deletes any previously-downloaded tenant logo so a stale
    /// file from a prior tenant-logo upload doesn't keep ghosting the AUMID
    /// IconUri after the admin clears the logo.
    /// </summary>
    public static async Task<string?> DownloadAsync(string? logoUrl, CancellationToken ct)
    {
        var dir = ConfigStore.GetConfigDirectory();

        if (string.IsNullOrWhiteSpace(logoUrl))
        {
            ClearExistingLogos(dir);
            return null;
        }

        if (!Uri.TryCreate(logoUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            DiagLog.Write($"TenantLogoStore.DownloadAsync: invalid url '{logoUrl}'");
            return null;
        }

        try
        {
            using var http = new HttpClient { Timeout = HttpTimeout };
            http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("ToastNotificationAgent", ThisAssembly.Version));

            using var resp = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                DiagLog.Write($"TenantLogoStore.DownloadAsync: server returned {(int)resp.StatusCode} for '{uri}'");
                return null;
            }

            // WSEC-M1: validate Content-Type against allowlist before buffering body.
            var mediaType = resp.Content.Headers.ContentType?.MediaType;
            if (string.IsNullOrWhiteSpace(mediaType) || !AllowedMediaTypes.Contains(mediaType))
            {
                DiagLog.Write($"TenantLogoStore.DownloadAsync: rejected Content-Type '{mediaType}' for '{uri}'");
                return null;
            }

            // WSEC-M1: pre-check Content-Length before buffering to avoid OOM on bogus servers.
            var contentLength = resp.Content.Headers.ContentLength;
            if (contentLength.HasValue && (contentLength.Value == 0 || contentLength.Value > MaxBytes))
            {
                DiagLog.Write($"TenantLogoStore.DownloadAsync: Content-Length {contentLength.Value} out of range for '{uri}'");
                return null;
            }

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0 || bytes.Length > MaxBytes)
            {
                DiagLog.Write($"TenantLogoStore.DownloadAsync: unexpected payload size {bytes.Length}");
                return null;
            }

            // WSEC-M1: validate magic bytes against the claimed media type.
            if (!ValidateMagicBytes(bytes, mediaType))
            {
                DiagLog.Write($"TenantLogoStore.DownloadAsync: magic-byte mismatch for Content-Type '{mediaType}' at '{uri}'");
                return null;
            }

            var ext = ResolveExtension(uri, mediaType);
            Directory.CreateDirectory(dir);
            ClearExistingLogos(dir);

            var path = Path.Combine(dir, $"{FilePrefix}{ext}");
            var temp = path + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes, ct);
            File.Move(temp, path, overwrite: true);

            DiagLog.Write($"TenantLogoStore.DownloadAsync: wrote {bytes.Length} bytes to '{path}'");
            return path;
        }
        catch (Exception ex)
        {
            DiagLog.Write($"TenantLogoStore.DownloadAsync: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    // WSEC-M1: derive extension from the validated Content-Type first, then fall
    // back to the URL path. This ensures the saved file extension matches what the
    // server actually served rather than what an attacker put in the URL.
    private static string ResolveExtension(Uri uri, string mediaType)
    {
        var extFromType = mediaType switch
        {
            "image/png"     => ".png",
            "image/jpeg"
            or "image/jpg"  => ".jpg",
            "image/gif"     => ".gif",
            "image/webp"    => ".webp",
            "image/svg+xml" => ".svg",
            _               => null,
        };
        if (extFromType is not null) return extFromType;

        var ext = Path.GetExtension(uri.LocalPath).ToLowerInvariant();
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" => ext,
            _ => ".png",
        };
    }

    // WSEC-M1: verify the first bytes of the payload match the signature for the
    // claimed media type. SVG is XML text — no fixed magic bytes, skipped.
    private static bool ValidateMagicBytes(byte[] bytes, string mediaType)
    {
        if (bytes.Length < 4) return false;
        return mediaType switch
        {
            "image/png"                => bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47,
            "image/jpeg" or "image/jpg" => bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            "image/gif"                => bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46,
            "image/webp"               => bytes.Length >= 12
                                          && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                                          && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50,
            "image/svg+xml"            => true,  // XML text, no fixed magic bytes
            _                          => false,
        };
    }

    private static void ClearExistingLogos(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var path in Directory.EnumerateFiles(dir, $"{FilePrefix}.*"))
        {
            try { File.Delete(path); }
            catch (Exception ex)
            {
                DiagLog.Write($"TenantLogoStore.ClearExistingLogos: failed to delete '{path}': {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
