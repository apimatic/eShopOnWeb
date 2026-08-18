using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// The view of a single notification returned to callers. Deliberately omits the destination number
/// (PII). Carries its own <see cref="NotificationId"/> because that is what operator endpoints act on,
/// and both the provider's identifier and the current delivery outcome so a caller can report on it.
/// </summary>
public class OrderNotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }

    /// <summary>Which order event this message was for (OrderPlaced, OrderDispatched, ...).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>A coarse delivery outcome derived from provider state: sent_failed, scheduled,
    /// queued/sent/delivered/undelivered/failed/canceled, or unknown.</summary>
    public string DeliveryOutcome { get; set; } = string.Empty;

    /// <summary>The provider's message identifier (Twilio sid), when one was produced.</summary>
    public string? ProviderMessageSid { get; set; }

    /// <summary>The provider's raw delivery status.</summary>
    public string? ProviderStatus { get; set; }

    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }

    /// <summary>True when no message could be handed to the provider (the operation still succeeded).</summary>
    public bool SendFailed { get; set; }

    /// <summary>True once the message body has been disposed of at the provider.</summary>
    public bool ContentRedacted { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static OrderNotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        DeliveryOutcome = DeriveOutcome(n),
        ProviderMessageSid = n.ProviderMessageSid,
        ProviderStatus = n.ProviderStatus,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        SendFailed = n.SendFailed,
        ContentRedacted = n.ContentRedacted,
        CreatedAt = n.CreatedAt,
        UpdatedAt = n.UpdatedAt
    };

    private static string DeriveOutcome(OrderNotification n)
    {
        if (n.SendFailed)
            return "send_failed";
        return string.IsNullOrEmpty(n.ProviderStatus) ? "unknown" : n.ProviderStatus!;
    }
}
