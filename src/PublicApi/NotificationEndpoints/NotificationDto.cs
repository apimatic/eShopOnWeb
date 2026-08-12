using System;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// The view of a single notification an operator/shopper sees. Carries the provider's identifier and
/// current delivery outcome — the state a later operator action acts on. The destination phone number
/// is deliberately never included in responses.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;

    /// <summary>Current delivery outcome as last known from the provider (e.g. queued, sent, delivered, undelivered, failed, scheduled, canceled).</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>The provider's own message identifier (Twilio SID), once the message was accepted.</summary>
    public string? ProviderMessageSid { get; set; }

    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }

    /// <summary>True for a follow-up queued to go out later (and therefore cancellable before it sends).</summary>
    public bool Scheduled { get; set; }

    /// <summary>True once the message text has been disposed of. The record still stands.</summary>
    public bool ContentDisposed { get; set; }

    public DateTimeOffset CreatedDate { get; set; }
    public DateTimeOffset? DateSent { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        Status = n.ProviderStatus,
        ProviderMessageSid = n.ProviderMessageSid,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        Scheduled = n.IsScheduled,
        ContentDisposed = n.ContentDisposed,
        CreatedDate = n.CreatedDate,
        DateSent = n.ProviderDateSent
    };
}
