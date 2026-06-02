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
            .Select(g => new DeviceGroupResponse(
                g.Id,
                g.Name,
                g.Description,
                g.Members.Count(m => m.Device.Status == DeviceStatus.Active),
                g.CreatedAt))
            .ToListAsync();

        return Ok(groups);
    }

    [HttpPost]
    public async Task<ActionResult<DeviceGroupResponse>> Create([FromBody] CreateDeviceGroupRequest req)
    {
        if (!IsAdmin()) return Forbid();

        var tenantId = GetTenantId();
        var name = req.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Group name is required.");

        if (await GroupNameExists(tenantId, name))
            return Conflict("A device group with this name already exists.");

        var group = new DeviceGroup
        {
            TenantId = tenantId,
            Name = name,
            Description = req.Description?.Trim(),
        };

        _db.DeviceGroups.Add(group);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(List), new { id = group.Id },
            new DeviceGroupResponse(group.Id, group.Name, group.Description, 0, group.CreatedAt));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<DeviceGroupResponse>> Update(Guid id, [FromBody] UpdateDeviceGroupRequest req)
    {
        if (!IsAdmin()) return Forbid();

        var tenantId = GetTenantId();
        var group = await _db.DeviceGroups.FirstOrDefaultAsync(g => g.Id == id && g.TenantId == GetTenantId());
        if (group is null) return NotFound();

        var name = req.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Group name is required.");

        if (await GroupNameExists(tenantId, name, id))
            return Conflict("A device group with this name already exists.");

        group.Name = name;
        group.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        group.DeviceCount = await ActiveMemberCount(id);
        await _db.SaveChangesAsync();

        return Ok(new DeviceGroupResponse(group.Id, group.Name, group.Description, group.DeviceCount, group.CreatedAt));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        if (!IsAdmin()) return Forbid();

        var group = await _db.DeviceGroups.FirstOrDefaultAsync(g => g.Id == id && g.TenantId == GetTenantId());
        if (group is null) return NotFound();

        _db.DeviceGroups.Remove(group);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("{id:guid}/members")]
    public async Task<ActionResult<IEnumerable<DeviceGroupMemberResponse>>> ListMembers(Guid id)
    {
        var exists = await _db.DeviceGroups.AnyAsync(g => g.Id == id && g.TenantId == GetTenantId());
        if (!exists) return NotFound();

        var members = await _db.DeviceGroupMembers
            .Where(m => m.DeviceGroupId == id && m.Device.Status != DeviceStatus.Decommissioned)
            .OrderBy(m => m.Device.DeviceName)
            .Select(m => new DeviceGroupMemberResponse(
                m.DeviceId, m.Device.DeviceName, m.Device.AgentVersion, m.AddedAt))
            .ToListAsync();

        return Ok(members);
    }

    [HttpPost("{id:guid}/members")]
    public async Task<IActionResult> AddMember(Guid id, [FromBody] AddMemberRequest req)
    {
        if (!IsAdmin()) return Forbid();

        var group = await _db.DeviceGroups.FirstOrDefaultAsync(g => g.Id == id && g.TenantId == GetTenantId());
        if (group is null) return NotFound("Device group not found.");

        var deviceExists = await _db.Devices
            .AnyAsync(d => d.Id == req.DeviceId && d.Status != DeviceStatus.Decommissioned);
        if (!deviceExists) return NotFound("Device not found.");

        var alreadyMember = await _db.DeviceGroupMembers
            .AnyAsync(m => m.DeviceGroupId == id && m.DeviceId == req.DeviceId);
        if (alreadyMember) return Conflict("Device is already in this group.");

        _db.DeviceGroupMembers.Add(new DeviceGroupMember
        {
            DeviceGroupId = id,
            DeviceId = req.DeviceId,
        });

        group.DeviceCount = await ActiveMemberCount(id) + 1;
        await _db.SaveChangesAsync();

        return Ok();
    }

    [HttpPut("{id:guid}/members")]
    public async Task<IActionResult> SetMembers(Guid id, [FromBody] SetDeviceGroupMembersRequest req)
    {
        if (!IsAdmin()) return Forbid();

        var group = await _db.DeviceGroups.FirstOrDefaultAsync(g => g.Id == id && g.TenantId == GetTenantId());
        if (group is null) return NotFound("Device group not found.");

        var requested = (req.DeviceIds ?? Array.Empty<Guid>())
            .Where(deviceId => deviceId != Guid.Empty)
            .Distinct()
            .ToList();

        var validDeviceIds = requested.Count == 0
            ? new List<Guid>()
            : await _db.Devices
                .Where(d => requested.Contains(d.Id) && d.Status != DeviceStatus.Decommissioned)
                .Select(d => d.Id)
                .ToListAsync();

        if (validDeviceIds.Count != requested.Count)
            return BadRequest("One or more devices do not exist in this tenant.");

        var valid = validDeviceIds.ToHashSet();
        var existing = await _db.DeviceGroupMembers
            .Where(m => m.DeviceGroupId == id)
            .ToListAsync();

        var existingIds = existing.Select(m => m.DeviceId).ToHashSet();
        var toRemove = existing.Where(m => !valid.Contains(m.DeviceId)).ToList();
        var toAdd = valid.Where(deviceId => !existingIds.Contains(deviceId)).ToList();

        if (toRemove.Count > 0)
            _db.DeviceGroupMembers.RemoveRange(toRemove);

        foreach (var deviceId in toAdd)
        {
            _db.DeviceGroupMembers.Add(new DeviceGroupMember
            {
                DeviceGroupId = id,
                DeviceId = deviceId,
            });
        }

        group.DeviceCount = valid.Count;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id:guid}/members/{deviceId:guid}")]
    public async Task<IActionResult> RemoveMember(Guid id, Guid deviceId)
    {
        if (!IsAdmin()) return Forbid();

        var member = await _db.DeviceGroupMembers
            .FirstOrDefaultAsync(m => m.DeviceGroupId == id
                && m.DeviceId == deviceId
                && m.DeviceGroup.TenantId == GetTenantId());
        if (member is null) return NotFound();

        var group = await _db.DeviceGroups.FirstOrDefaultAsync(g => g.Id == id && g.TenantId == GetTenantId());
        _db.DeviceGroupMembers.Remove(member);

        if (group is not null)
        {
            group.DeviceCount = await _db.DeviceGroupMembers
                .CountAsync(m => m.DeviceGroupId == id
                    && m.DeviceId != deviceId
                    && m.Device.Status == DeviceStatus.Active);
        }

        await _db.SaveChangesAsync();

        return NoContent();
    }

    private Guid GetTenantId() =>
        Guid.Parse(User.FindFirstValue("tenantId")!);

    private Task<bool> GroupNameExists(Guid tenantId, string name, Guid? excludeId = null)
    {
        var normalized = name.ToLower();
        return _db.DeviceGroups
            .AnyAsync(g => g.TenantId == tenantId
                && g.Name.ToLower() == normalized
                && (excludeId == null || g.Id != excludeId.Value));
    }

    private Task<int> ActiveMemberCount(Guid groupId) =>
        _db.DeviceGroupMembers
            .CountAsync(m => m.DeviceGroupId == groupId && m.Device.Status == DeviceStatus.Active);

    private bool IsAdmin()
    {
        var role = Enum.TryParse<UserRole>(User.FindFirstValue("role"), out var r) ? r : UserRole.Technician;
        return role >= UserRole.Admin;
    }
}
