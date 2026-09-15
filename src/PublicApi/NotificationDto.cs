using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// How one notification about an order got on: which message it was, the provider's identifier and
/// the current delivery outcome. This is what the operator endpoints act on via <see cref="NotificationId"/>.
/// The destination number is deliberately not exposed here.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }

    /// <summary>Why the message was sent (OrderPlaced, OrderDispatched, OrderCancelled, DeliveryFollowUp, Resend).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Normalized delivery outcome (NotSent, Queued, Scheduled, Sending, Sent, Delivered, Undelivered, Failed, Canceled, Unknown).</summary>
    public string DeliveryStatus { get; set; } = string.Empty;

    /// <summary>The provider's message identifier (SID), once accepted.</summary>
    public string? ProviderMessageSid { get; set; }

    /// <summary>The provider's raw status string.</summary>
    public string? ProviderStatus { get; set; }

    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Message text (null once its content has been disposed of).</summary>
    public string? Body { get; set; }

    public DateTimeOffset? ScheduledFor { get; set; }
    public bool ContentDisposed { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static NotificationDto FromEntity(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        DeliveryStatus = n.DeliveryStatus.ToString(),
        ProviderMessageSid = n.ProviderMessageSid,
        ProviderStatus = n.ProviderStatus,
        ErrorCode = n.ErrorCode,
        ErrorMessage = n.ErrorMessage,
        Body = n.Body,
        ScheduledFor = n.ScheduledFor,
        ContentDisposed = n.ContentDisposed,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt
    };
}
