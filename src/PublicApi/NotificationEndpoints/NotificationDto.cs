using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// The operator/shopper view of a single notification and what became of it. The destination number is
/// deliberately not exposed.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>The current delivery outcome (the provider's status, or a local sentinel when never accepted).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The provider's identifier for the message, when one was issued.</summary>
    public string? ProviderMessageSid { get; set; }

    public DateTimeOffset? ScheduledFor { get; set; }
    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ContentDisposed { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.DeliveryStatus,
        ProviderMessageSid = n.ProviderMessageSid,
        ScheduledFor = n.ScheduledFor,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        ContentDisposed = n.ContentDisposed,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt
    };
}
