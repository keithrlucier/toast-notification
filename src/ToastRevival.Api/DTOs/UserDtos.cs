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

// AA-M10: Password removed from InviteUserRequest — never accepted from client.
// A secure temp password is generated internally in UsersController.Invite.
public record InviteUserRequest(
    [Required, EmailAddress] string Email,
    UserRole Role = UserRole.Technician);

public record UpdateRoleRequest([Required] UserRole Role);
