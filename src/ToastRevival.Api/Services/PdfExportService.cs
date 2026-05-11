using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ToastRevival.Api.Models;

namespace ToastRevival.Api.Services;

public interface IPdfExportService
{
    byte[] GenerateAuditLogPdf(IList<AuditLog> logs, string tenantName, int days);
    byte[] GenerateDeliveryReportPdf(Notification notification, IList<NotificationDelivery> deliveries, string tenantName);
}

public class PdfExportService : IPdfExportService
{
    private const string Amber  = "#F59E0B";
    private const string Dark   = "#1A1D27";
    private const string Light  = "#F5F7F9";
    private const string White  = "#FFFFFF";
    private const string Muted  = "#7A7A92";
    private const string Error  = "#DC2626";

    public byte[] GenerateAuditLogPdf(IList<AuditLog> logs, string tenantName, int days)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Dark));

                page.Header().Column(col =>
                {
                    col.Item()
                        .BorderBottom(3).BorderColor(Amber).PaddingBottom(8)
                        .Row(row =>
                        {
                            row.RelativeItem()
                                .Text("Toast Notification — Audit Log")
                                .FontSize(15).Bold().FontColor(Dark);
                            row.AutoItem()
                                .AlignRight()
                                .Text($"Exported {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC")
                                .FontSize(8).FontColor(Muted);
                        });
                    col.Item().PaddingTop(4)
                        .Text($"Last {days} days  •  Tenant: {tenantName}  •  {logs.Count} entries")
                        .FontSize(8).FontColor(Muted);
                });

                page.Content().PaddingTop(12).Table(table =>
                {
                    table.ColumnsDefinition(cols =>
                    {
                        cols.RelativeColumn(2.2f);
                        cols.RelativeColumn(2.5f);
                        cols.RelativeColumn(1.8f);
                        cols.RelativeColumn(2.5f);
                        cols.RelativeColumn(2.0f);
                        cols.RelativeColumn(1.5f);
                    });

                    table.Header(header =>
                    {
                        foreach (var h in new[] { "Timestamp", "Action", "Resource Type", "Resource ID", "User", "IP Address" })
                        {
                            header.Cell()
                                .Background(Amber).Padding(6)
                                .Text(h).Bold().FontColor(White).FontSize(8);
                        }
                    });

                    var i = 0;
                    foreach (var log in logs)
                    {
                        var bg = i++ % 2 == 0 ? White : Light;
                        Cell(table, bg, log.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"), mono: true);
                        Cell(table, bg, log.Action);
                        Cell(table, bg, log.ResourceType);
                        Cell(table, bg, log.ResourceId ?? "—", mono: true);
                        Cell(table, bg, log.UserId.HasValue ? log.UserId.Value.ToString()[..8] + "…" : "—", mono: true);
                        Cell(table, bg, log.IpAddress ?? "—", mono: true);
                    }
                });

                page.Footer().AlignRight().Text(x =>
                {
                    x.Span("Page ").FontSize(8).FontColor(Muted);
                    x.CurrentPageNumber().FontSize(8).FontColor(Muted);
                    x.Span(" of ").FontSize(8).FontColor(Muted);
                    x.TotalPages().FontSize(8).FontColor(Muted);
                });
            });
        }).GeneratePdf();
    }

    public byte[] GenerateDeliveryReportPdf(Notification notification, IList<NotificationDelivery> deliveries, string tenantName)
    {
        var total     = deliveries.Count;
        var delivered = deliveries.Count(d => d.Status == DeliveryStatus.Delivered);
        var clicked   = deliveries.Count(d => d.Status == DeliveryStatus.Clicked);
        var failed    = deliveries.Count(d => d.Status == DeliveryStatus.Failed);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(Dark));

                page.Header().Column(col =>
                {
                    col.Item()
                        .BorderBottom(3).BorderColor(Amber).PaddingBottom(8)
                        .Row(row =>
                        {
                            row.RelativeItem()
                                .Text("Delivery Report")
                                .FontSize(15).Bold().FontColor(Dark);
                            row.AutoItem()
                                .AlignRight()
                                .Text($"Exported {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC")
                                .FontSize(8).FontColor(Muted);
                        });
                    col.Item().PaddingTop(4)
                        .Text($"{notification.Title}  •  Sent {notification.SentAt?.ToString("yyyy-MM-dd HH:mm") ?? "N/A"}  •  Tenant: {tenantName}")
                        .FontSize(8).FontColor(Muted);
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    // Summary row
                    col.Item().PaddingBottom(16).Row(row =>
                    {
                        SummaryCell(row, "Total Targets", total.ToString(), Dark);
                        SummaryCell(row, "Delivered",     delivered.ToString(), "#16A34A");
                        SummaryCell(row, "Clicked",       clicked.ToString(), "#0D9488");
                        SummaryCell(row, "Failed",        failed.ToString(), Error);
                    });

                    // Delivery table
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn(2.5f);
                            cols.RelativeColumn(1.5f);
                            cols.RelativeColumn(2.0f);
                            cols.RelativeColumn(2.0f);
                            cols.RelativeColumn(2.5f);
                        });

                        table.Header(header =>
                        {
                            foreach (var h in new[] { "Device Name", "Status", "Delivered At", "Action", "Error" })
                            {
                                header.Cell()
                                    .Background(Amber).Padding(6)
                                    .Text(h).Bold().FontColor(White).FontSize(8);
                            }
                        });

                        var i = 0;
                        foreach (var d in deliveries)
                        {
                            var bg       = i++ % 2 == 0 ? White : Light;
                            var hasError = !string.IsNullOrEmpty(d.ErrorMessage);
                            Cell(table, bg, d.Device?.DeviceName ?? "—");
                            Cell(table, bg, d.Status.ToString());
                            Cell(table, bg, d.DeliveredAt?.ToString("yyyy-MM-dd HH:mm") ?? "—");
                            Cell(table, bg, d.Action ?? "—");
                            table.Cell().Background(bg).Padding(5)
                                .Text(d.ErrorMessage ?? "—")
                                .FontSize(8)
                                .FontColor(hasError ? Error : Muted);
                        }
                    });
                });

                page.Footer().AlignRight().Text(x =>
                {
                    x.Span("Page ").FontSize(8).FontColor(Muted);
                    x.CurrentPageNumber().FontSize(8).FontColor(Muted);
                    x.Span(" of ").FontSize(8).FontColor(Muted);
                    x.TotalPages().FontSize(8).FontColor(Muted);
                });
            });
        }).GeneratePdf();
    }

    // Helpers to reduce repetition
    private static void Cell(TableDescriptor table, string bg, string value, bool mono = false)
    {
        var cell = table.Cell().Background(bg).Padding(5);
        if (mono)
            cell.DefaultTextStyle(x => x.FontFamily("Courier New").FontSize(7.5f)).Text(value);
        else
            cell.Text(value).FontSize(8.5f);
    }

    private static void SummaryCell(RowDescriptor row, string label, string value, string color)
    {
        row.RelativeItem()
            .Border(1).BorderColor("#E5E7EB")
            .Padding(12)
            .Column(c =>
            {
                c.Item().Text(label).FontSize(8).FontColor(Muted);
                c.Item().Text(value).FontSize(20).Bold().FontColor(color);
            });
    }
}
