using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.DTOs;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("tenant-per-minute")]
public class DeviceGroupsController : ControllerBase
{
    private readonly AppDbContext _db;

    public DeviceGroupsController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<DeviceGroupResponse>>> List()
    {
        var groups = await _db.DeviceGroups
            .OrderBy(g => g.Name)
            .Select(g => new DeviceGroupResponse(g.Id, g.Name, g.Description, g.DeviceCount, g.CreatedAt))
            .ToListAsync();

        return Ok(groups);
    }

    [HttpPost]
    public async Task<ActionResult<DeviceGroupResponse>> Create([FromBody] CreateDeviceGroupRequest req)
    {
        if (!IsAdmin()) return Forbid();

        var group = new DeviceGroup
        {
            TenantId = GetTenantId(),
            Name = req.Name.Trim(),
            Description = req.Description?.Trim(),
        };

        _db.DeviceGroups.Add(group);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new { id = group.Id },
            new DeviceGroupResponse(group.Id, group.Name, group.Description, 0, group.CreatedAt));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!IsAdmin()) return Forbid();

        var group = await _db.DeviceGroups.FindAsync(id);
        if (group is null) return NotFound();

        _db.DeviceGroups.Remove(group);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<IEnumerable<DeviceGroupMemberResponse>>> ListMembers(Guid id)
    {
        var exists = await _db.DeviceGroups.AnyAsync(g => g.Id == id);
        if (!exists) return NotFound();

        var members = await _db.DeviceGroupMembers
            .Where(m => m.DeviceGroupId == id)
            .Select(m => new DeviceGroupMemberResponse(
                m.DeviceId, m.Device.DeviceName, m.Device.AgentVersion, m.AddedAt))
            .ToListAsync();

        return Ok(members);
    }

    [HttpPost("{id:guid}/members")]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddMemberRequest req)
    {
        if (!IsAdmin()) return Forbid();

        var group = await _db.DeviceGroups.FindAsync(id);
        if (group is null) return NotFound("Device group not found.");

        var deviceExists = await _db.Devices.AnyAsync(d => d.Id == req.DeviceId);
        if (!deviceExists) return NotFound("Device not found.");

        var alreadyMember = await _db.DeviceGroupMembers
            .AnyAsync(m => m.DeviceGroupId == id && m.DeviceId == req.DeviceId);
        if (alreadyMember) return Conflict("Device is already in this group.");

        _db.DeviceGroupMembers.Add(new DeviceGroupMember
        {
            DeviceGroupId = id,
            DeviceId = req.DeviceId,
        });

        group.DeviceCount++;
        await _db.SaveChangesAsync();

        return Ok();
    }

    [HttpDelete("{id:guid}/members/{deviceId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid deviceId)
    {
        if (!IsAdmin()) return Forbid();

        var member = await _db.DeviceGroupMembers
            .FirstOrDefaultAsync(m => m.DeviceGroupId == id && m.DeviceId == deviceId);
        if (member is null) return NotFound();

        var group = await _db.DeviceGroups.FindAsync(id);
        _db.DeviceGroupMembers.Remove(member);

        if (group is not null && group.DeviceCount > 0)
            group.DeviceCount--;

        await _db.SaveChangesAsync();

        return NoContent();
    }

    private Guid GetTenantId() =>
        Guid.Parse(User.FindFirstValue("tenantId")!);

    private bool IsAdmin()
    {
        var role = Enum.TryParse<UserRole>(User.FindFirstValue("role"), out var r) ? r : UserRole.Technician;
        return role >= UserRole.Admin;
    }
}
