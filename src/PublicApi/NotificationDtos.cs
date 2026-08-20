using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;
using Microsoft.eShopWeb.ApplicationCore.Messaging;

namespace Microsoft.eShopWeb.PublicApi;

public class NotificationDto
{
    public int NotificationId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string DeliveryStatus { get; set; } = string.Empty;
    public string? ProviderSid { get; set; }
    public string? Body { get; set; }
    public bool ContentDisposed { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public string? DateSent { get; set; }

    public static NotificationDto From(OrderNotification notification) => new()
    {
        NotificationId = notification.Id,
        Kind = notification.Kind.ToString(),
        DeliveryStatus = notification.DeliveryStatus,
        ProviderSid = notification.ProviderSid,
        Body = notification.ContentDisposed ? null : notification.Body,
        ContentDisposed = notification.ContentDisposed,
        ErrorCode = notification.ErrorCode,
        ErrorMessage = notification.ErrorMessage,
        CreatedAt = notification.CreatedAt,
        ScheduledAt = notification.ScheduledAt,
        DateSent = notification.ProviderDateSent
    };
}

public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTimeOffset OrderDate { get; set; }
    public decimal Total { get; set; }
    public List<OrderLineDto> Items { get; set; } = new();
    public List<NotificationDto> Notifications { get; set; } = new();
}

public class OrderLineDto
{
    public int CatalogItemId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public bool Truncated { get; set; }
    public List<ReconciliationRowDto> Matched { get; set; } = new();
    public List<ReconciliationRowDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationRowDto> EshopOnly { get; set; } = new();

    public static ReconciliationResponse Create(ReconciliationReport report) => new()
    {
        From = report.From,
        To = report.To,
        FromNumber = report.FromNumber,
        Truncated = report.Truncated,
        Matched = Map(report.Matched),
        ProviderOnly = Map(report.ProviderOnly),
        EshopOnly = Map(report.EshopOnly)
    };

    private static List<ReconciliationRowDto> Map(IReadOnlyList<ReconciliationRow> rows)
    {
        var list = new List<ReconciliationRowDto>(rows.Count);
        foreach (var row in rows)
        {
            list.Add(new ReconciliationRowDto
            {
                NotificationId = row.NotificationId,
                ProviderSid = row.ProviderSid,
                Status = row.Status,
                Body = row.Body,
                DateSent = row.DateSent,
                Source = row.Direction
            });
        }

        return list;
    }
}

public class ReconciliationRowDto
{
    public int? NotificationId { get; set; }
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
    public string? Body { get; set; }
    public string? DateSent { get; set; }
    public string? Source { get; set; }
}
