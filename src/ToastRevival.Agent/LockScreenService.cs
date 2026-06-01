using System.Net.Http.Headers;
using System.Security.Cryptography;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System.UserProfile;

namespace ToastRevival.Agent;

/// <summary>
/// M12 — applies the tenant's branded lock screen image to the per-user lock
/// screen (Win+L, screensaver, lid close) via the WinRT
/// <see cref="LockScreen"/> API. Runs in user context, no elevation.
///
/// Save-before-modify: the genuine original lock screen image is snapshotted to
/// <c>lockscreen_original.jpg</c> on the FIRST apply and restored when branding
/// is later disabled. The downloaded image is hash-checked so an unchanged image
/// is not re-applied on every startup (the OS persists the lock screen across
/// reboots).
///
/// Best-effort throughout: any failure (download, WinRT call blocked by GPO,
/// IO) is logged and swallowed — the device keeps whatever it currently shows.
/// Pre-login greeter branding is out of scope (needs SYSTEM/PersonalizationCSP).
/// </summary>
internal static class LockScreenService
{
    private const string CurrentFile  = "lockscreen.jpg";
    private const string OriginalFile = "lockscreen_original.jpg";
    private const string HashFile     = "lockscreen.hash";
    private const long   MaxBytes     = 5 * 1024 * 1024;
    private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(30);

    public static async Task ApplyAsync(LockScreenConfig? config, string serverUrl, CancellationToken ct)
    {
        var dir          = ConfigStore.GetConfigDirectory();
        var currentPath  = Path.Combine(dir, CurrentFile);
        var originalPath  = Path.Combine(dir, OriginalFile);
        var hashPath     = Path.Combine(dir, HashFile);

        // Disabled (or no image) → restore the snapshot if we ever applied one.
        if (config is not { Enabled: true } || string.IsNullOrWhiteSpace(config.ImageUrl))
        {
            await RestoreIfNeededAsync(currentPath, originalPath, hashPath);
            return;
        }

        if (!Uri.TryCreate(config.ImageUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            DiagLog.Write($"LockScreen: invalid image url '{config.ImageUrl}'");
            return;
        }

        // FIX-AGT-4 (2026-06-01): host-pin the fetch to our trusted ServerUrl. The
        // appearance channel is TLS-authenticated but UNSIGNED (no HMAC, unlike the
        // toast path), so a TLS-trust compromise could repoint ImageUrl at an attacker
        // host. Re-base the image PATH onto ServerUrl so the GET can only ever hit our
        // own origin, whatever host the payload claims — the image is same-origin
        // (/assets/lockscreen/, served by the same server the agent already trusts).
        // (End-to-end HMAC signing of the whole appearance config is the full fix — a
        // versioned server+agent rollout; see REVIEW_LEDGER AGT-4 remainder, owner: Keith.)
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri))
        {
            DiagLog.Write($"LockScreen: invalid server url '{serverUrl}'");
            return;
        }
        if (!string.Equals(uri.Host, serverUri.Host, StringComparison.OrdinalIgnoreCase))
            DiagLog.Write($"LockScreen: image host '{uri.Host}' != server '{serverUri.Host}' — pinning to server origin.");
        uri = new Uri(serverUri, uri.PathAndQuery);

        byte[] bytes;
        try
        {
            using var http = new HttpClient { Timeout = HttpTimeout };
            http.DefaultRequestHeaders.UserAgent.Add(
                new ProductInfoHeaderValue("ToastNotificationAgent", ThisAssembly.Version));

            using var resp = await http.GetAsync(uri, ct);
            if (!resp.IsSuccessStatusCode)
            {
                DiagLog.Write($"LockScreen: download returned {(int)resp.StatusCode} for '{uri}'");
                return;
            }
            var mediaType = resp.Content.Headers.ContentType?.MediaType;
            if (mediaType is not ("image/jpeg" or "image/png"))
            {
                DiagLog.Write($"LockScreen: unexpected content-type '{mediaType}' for '{uri}'");
                return;
            }
            bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            DiagLog.Write($"LockScreen: download failed: {ex.GetType().Name}: {ex.Message}");
            return;
        }

        if (bytes.Length == 0 || bytes.Length > MaxBytes)
        {
            DiagLog.Write($"LockScreen: unexpected payload size {bytes.Length}");
            return;
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes));

        // Idempotent: image already applied (file + snapshot + matching hash all
        // present) → skip. Re-setting an unchanged lock screen each startup is waste.
        if (File.Exists(currentPath) && File.Exists(originalPath)
            && File.Exists(hashPath) && SafeReadText(hashPath) == hash)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(dir);

            // Snapshot the genuine original ONCE, before we ever overwrite it.
            if (!File.Exists(originalPath))
                await SnapshotCurrentLockScreenAsync(originalPath);

            // Write the new image atomically, then point the lock screen at it.
            var tmp = currentPath + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes, ct);
            File.Move(tmp, currentPath, overwrite: true);

            await SetLockScreenAsync(currentPath);
            File.WriteAllText(hashPath, hash);
            DiagLog.Write($"LockScreen: applied {bytes.Length} bytes from '{uri}'");
        }
        catch (Exception ex)
        {
            DiagLog.Write($"LockScreen: apply failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Public revert entry point for the removal / uninstall path
    /// (<c>--revert-appearance</c> agent mode and the remote-uninstall hub
    /// command). Restores the genuine original lock screen if we have a
    /// snapshot, otherwise falls back to the Windows default, then clears all
    /// branding state. Best-effort; never throws. Calling this is identical to
    /// disabling the feature, so it shares the exact restore path.
    /// </summary>
    // Disable/restore path: config is null, so ApplyAsync early-returns before the
    // host-pin ever reads serverUrl — an empty origin here is intentional and unused.
    public static Task RevertAsync(CancellationToken ct) => ApplyAsync(null, string.Empty, ct);

    private static async Task RestoreIfNeededAsync(string currentPath, string originalPath, string hashPath)
    {
        var brandedByUs = File.Exists(originalPath) || File.Exists(currentPath) || File.Exists(hashPath);
        if (!brandedByUs) return; // never applied → nothing to undo, leave the device alone

        try
        {
            if (File.Exists(originalPath))
            {
                await SetLockScreenAsync(originalPath);
                DiagLog.Write("LockScreen: restored original lock screen on disable/revert.");
            }
            else
            {
                // We branded this device (current/hash present) but the genuine
                // snapshot is gone (e.g. config dir partially wiped, or the agent
                // was reinstalled while branding was active). Do-no-harm fallback:
                // point the lock screen at the Windows default image so the device
                // is never left stuck on our branding with no way back.
                await RestoreWindowsDefaultAsync();
            }
        }
        catch (Exception ex)
        {
            DiagLog.Write($"LockScreen: restore failed: {ex.GetType().Name}: {ex.Message}");
            // Fall through and clear state regardless — leaving the snapshot in place
            // would re-trigger a restore attempt on every subsequent startup.
        }

        SafeDelete(originalPath);
        SafeDelete(currentPath);
        SafeDelete(hashPath);
    }

    /// <summary>
    /// Sets the lock screen to the stock Windows default image. Used only as the
    /// snapshot-lost fallback in <see cref="RestoreIfNeededAsync"/>. img100.jpg
    /// ships on every Windows 10/11 SKU; img105.jpg is the Win11 backup. If
    /// neither exists the device keeps whatever it currently shows (no-op).
    /// </summary>
    private static async Task RestoreWindowsDefaultAsync()
    {
        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (var candidate in new[] { "img100.jpg", "img105.jpg", "img0.jpg" })
        {
            var path = Path.Combine(windir, "Web", "Screen", candidate);
            if (!File.Exists(path)) continue;
            await SetLockScreenAsync(path);
            DiagLog.Write($"LockScreen: snapshot missing — restored Windows default '{candidate}'.");
            return;
        }
        DiagLog.Write("LockScreen: snapshot missing and no Windows default image found — leaving as-is.");
    }

    private static async Task SetLockScreenAsync(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        await LockScreen.SetImageFileAsync(file);
    }

    /// <summary>
    /// Copies the current lock screen image bytes to <paramref name="originalPath"/>.
    /// .NET 8 dropped the IRandomAccessStream→Stream bridge (AsStreamForRead), so
    /// the read goes through a WinRT DataReader.
    /// </summary>
    private static async Task SnapshotCurrentLockScreenAsync(string originalPath)
    {
        try
        {
            using var stream = LockScreen.GetImageStream();
            if (stream is null || stream.Size == 0) return;

            using var reader = new DataReader(stream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)stream.Size);
            var buffer = new byte[stream.Size];
            reader.ReadBytes(buffer);

            await File.WriteAllBytesAsync(originalPath, buffer);
            DiagLog.Write($"LockScreen: snapshotted original ({buffer.Length} bytes).");
        }
        catch (Exception ex)
        {
            // No snapshot → no restore later (RestoreIfNeededAsync no-ops without it).
            DiagLog.Write($"LockScreen: snapshot failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string? SafeReadText(string path)
    {
        try { return File.ReadAllText(path).Trim(); } catch { return null; }
    }

    private static void SafeDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best-effort */ }
    }
}
