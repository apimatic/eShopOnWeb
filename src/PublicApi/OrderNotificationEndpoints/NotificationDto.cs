using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

/// <summary>
/// What was sent for an order and what became of it. The destination number is deliberately omitted —
/// a shopper's number is never exposed here.
/// </summary>
public class NotificationDto
{
    /// <summary>The identifier the operator endpoints act on.</summary>
    public int NotificationId { get; set; }

    public int OrderId { get; set; }

    public string Kind { get; set; } = string.Empty;

    /// <summary>The provider's own identifier for the message.</summary>
    public string? MessageSid { get; set; }

    /// <summary>The provider's current delivery outcome.</summary>
    public string? Status { get; set; }

    public int? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>Set when the message could not be handed to the provider at all (the order still succeeded).</summary>
    public string? FailureReason { get; set; }

    /// <summary>When set, the provider is holding this message to send at a future time.</summary>
    public DateTimeOffset? ScheduledSendAt { get; set; }

    public bool ContentRedacted { get; set; }

    public int? ResendOfNotificationId { get; set; }

    public DateTimeOffset CreatedDate { get; set; }

    public DateTimeOffset UpdatedDate { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        MessageSid = n.ProviderMessageSid,
        Status = n.ProviderStatus,
        ErrorCode = n.ProviderErrorCode,
        ErrorMessage = n.ProviderErrorMessage,
        FailureReason = n.FailureReason,
        ScheduledSendAt = n.ScheduledSendAt,
        ContentRedacted = n.ContentRedacted,
        ResendOfNotificationId = n.ResendOfNotificationId,
        CreatedDate = n.CreatedDate,
        UpdatedDate = n.UpdatedDate
    };
}
