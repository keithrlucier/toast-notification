using System.Security.Claims;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Extensions;

/// <summary>
/// ARCH-M1: Shared IsAdmin/IsPlatformAdmin helpers extracted from per-controller
/// private copies, which had inconsistent implementations. Single source of truth.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    /// <summary>
    /// Returns true when the principal has Admin, SuperAdmin, or platform admin privileges.
    /// Checks both the "role" claim and the "platformAdmin" claim.
    /// </summary>
    public static bool IsAdmin(this ClaimsPrincipal user)
    {
        var role = user.FindFirstValue("role") ?? user.FindFirstValue(ClaimTypes.Role) ?? "";
        var isAdminRole = Enum.TryParse<UserRole>(role, out var r) && r >= UserRole.Admin;
        return isAdminRole || user.FindFirstValue("platformAdmin") == "true";
    }

    /// <summary>
    /// Returns true only when the principal has the platform admin privilege
    /// (the "platformAdmin=true" claim, not merely a high tenant role).
    /// </summary>
    public static bool IsPlatformAdmin(this ClaimsPrincipal user) =>
        user.FindFirstValue("platformAdmin") == "true";
}
