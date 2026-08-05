namespace ToastRevival.Api.Utilities;

/// <summary>
/// Shared magic-byte (file signature) validation for uploaded images, keyed off the
/// already-validated file extension. Mirrors the agent's TenantLogoStore.ValidateMagicBytes
/// (WSEC-M1) so a renamed non-image or a polyglot (valid header + arbitrary trailing data)
/// cannot be persisted and served. Used by AssetsController (ASSET-L1) and the TenantController
/// logo / lock-screen uploads (Routes-L2), which previously validated extension + size only.
/// </summary>
public static class ImageSignature
{
    public static bool HasValidMagicBytes(byte[] bytes, string ext)
    {
        if (bytes.Length < 4) return false;
        return ext switch
        {
            ".png"            => bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47,
            ".jpg" or ".jpeg" => bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            ".gif"            => bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46,
            ".webp"           => bytes.Length >= 12
                                 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                                 && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50,
            _                 => false,
        };
    }
}
