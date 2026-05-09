using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using ToastRevival.Api.Data;
using ToastRevival.Api.Models;
using ToastRevival.Api.Services;

namespace ToastRevival.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[EnableRateLimiting("tenant-per-minute")]
public class AuditController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPdfExportService _pdf;

    public AuditController(AppDbContext db, IPdfExportService pdf)
    {
        _db = db;
        _pdf = pdf;
    }

    /// <summary>
    /// GET /api/audit?days=30&amp;page=1&amp;pageSize=50
    /// Admin-only. Returns paginated audit log entries for the tenant.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int days     = 30,
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 50)
    {
        if (!IsAdmin()) return Forbid();

        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 90));
        var p    = Math.Max(1, page);
        var size = Math.Clamp(pageSize, 1, 100);

        var logs = await _db.AuditLogs
            .Where(l => l.Timestamp >= since)
            .OrderByDescending(l => l.Timestamp)
            .Skip((p - 1) * size)
            .Take(size)
            .Select(l => new
            {
                l.Id,
                l.Action,
                l.ResourceType,
                l.ResourceId,
                UserId    = l.UserId.HasValue ? l.UserId.ToString() : null,
                l.IpAddress,
                l.Timestamp,
            })
            .ToListAsync();

        return Ok(logs);
    }

    /// <summary>
    /// GET /api/audit/export?format=csv&amp;days=30
    /// Admin-only. Downloads the full audit log (no pagination cap) as CSV or PDF.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string format = "csv",
        [FromQuery] int    days   = 30)
    {
        if (!IsAdmin()) return Forbid();

        var since = DateTime.UtcNow.AddDays(-Math.Clamp(days, 1, 90));

        var logs = await _db.AuditLogs
            .Where(l => l.Timestamp >= since)
            .OrderByDescending(l => l.Timestamp)
            .ToListAsync();

        var tenantId   = Guid.Parse(User.FindFirstValue("tenantId")!);
        var tenantName = await _db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync() ?? "Unknown";

        if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
        {
            var pdfBytes = _pdf.GenerateAuditLogPdf(logs, tenantName, days);
            var fileName = $"audit-log-{DateTime.UtcNow:yyyy-MM-dd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }

        // Default: CSV
        var csv      = BuildAuditCsv(logs);
        var csvBytes = Encoding.UTF8.GetBytes(csv);
        return File(csvBytes, "text/csv", $"audit-log-{DateTime.UtcNow:yyyy-MM-dd}.csv");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private bool IsAdmin()
    {
        var role = Enum.TryParse<UserRole>(User.FindFirstValue("role"), out var r) ? r : UserRole.Technician;
        return role >= UserRole.Admin;
    }

    private static string BuildAuditCsv(IList<AuditLog> logs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,Action,ResourceType,ResourceId,UserId,IpAddress");

        foreach (var l in logs)
        {
            sb.AppendLine(string.Join(",",
                CsvCell(l.Timestamp.ToString("o")),
                CsvCell(l.Action),
                CsvCell(l.ResourceType),
                CsvCell(l.ResourceId ?? ""),
                CsvCell(l.UserId?.ToString() ?? ""),
                CsvCell(l.IpAddress ?? "")));
        }

        return sb.ToString();
    }

    private static string CsvCell(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
