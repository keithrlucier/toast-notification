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

            using var resp = await http.GetAsync(uri, ct);
            if (!resp.IsSuccessStatusCode)
            {
                DiagLog.Write($"TenantLogoStore.DownloadAsync: server returned {(int)resp.StatusCode} for '{uri}'");
                return null;
            }

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0 || bytes.Length > MaxBytes)
            {
                DiagLog.Write($"TenantLogoStore.DownloadAsync: unexpected payload size {bytes.Length}");
                return null;
            }

            var ext = ResolveExtension(uri);
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

    private static string ResolveExtension(Uri uri)
    {
        var ext = Path.GetExtension(uri.LocalPath).ToLowerInvariant();
        // Whitelist the formats Windows can render at the AUMID IconUri size.
        // TenantController.UploadLogo accepts .png/.jpg/.jpeg/.gif/.webp; the
        // first three render reliably as the tiny attribution icon. GIF/WEBP
        // still write to disk but may render as a generic placeholder — log
        // a note so an MSP whose icon stops appearing knows where to look.
        return ext switch
        {
            ".png" or ".jpg" or ".jpeg" => ext,
            _ => ".png",
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
