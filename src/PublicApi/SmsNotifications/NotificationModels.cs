using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.SmsNotifications;

/// <summary>
/// What was sent (or attempted) for an order, and what became of it. Deliberately does not carry the
/// shopper's destination number.
/// </summary>
public class NotificationDto
{
    /// <summary>Identifier the operator endpoints (resend / dispose) act on.</summary>
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Type { get; set; } = string.Empty;

    /// <summary>The message text; null once the content has been disposed of.</summary>
    public string? Body { get; set; }

    /// <summary>The provider's own identifier for this message, when it accepted it.</summary>
    public string? ProviderSid { get; set; }

    /// <summary>The provider's current delivery outcome (or <c>not_sent</c> when never accepted).</summary>
    public string? Status { get; set; }

    public int? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ContentDisposed { get; set; }
    public DateTimeOffset? ScheduledSendAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static NotificationDto From(Notification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Type = n.Type.ToString(),
        Body = n.Body,
        ProviderSid = n.ProviderSid,
        Status = n.ProviderStatus,
        ErrorCode = n.ProviderErrorCode,
        ErrorMessage = n.ProviderErrorMessage,
        ContentDisposed = n.ContentDisposed,
        ScheduledSendAt = n.ScheduledSendAt,
        SentAt = n.ProviderSentAt,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt
    };
}

/// <summary>An order together with where each of its notifications got to.</summary>
public class OrderSummaryDto
{
    public int OrderId { get; set; }
    public DateTimeOffset OrderDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Total { get; set; }
    public List<NotificationDto> Notifications { get; set; } = new();
}
