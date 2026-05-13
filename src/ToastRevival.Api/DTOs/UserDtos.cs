using System.ComponentModel.DataAnnotations;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.DTOs;

public record UserResponse(
    Guid Id,
    string Email,
    string Role,
    bool MfaEnabled,
    DateTime? LastLogin,
    DateTime CreatedAt);

public record InviteUserRequest(
    [Required, EmailAddress] string Email,
    [Required, MinLength(8)] string Password,
    UserRole Role = UserRole.Technician);

public record UpdateRoleRequest([Required] UserRole Role);
