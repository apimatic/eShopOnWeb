using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Record of a single SMS sent (or scheduled) for an order, carrying the provider-owned
/// state (message SID and last known delivery outcome) so later requests can act on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationType NotificationType { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio Message SID).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Last known delivery outcome reported by the provider.</summary>
    public string? ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    public bool ContentDisposed { get; private set; }

    /// <summary>True when the provider accepted the send request; false when it failed locally.</summary>
    public bool AcceptedByProvider { get; private set; }

    /// <summary>Set for messages queued with the provider for future delivery.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Caller-supplied key for operator re-sends; prevents duplicate sends.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Set when this notification was produced by re-sending an earlier one.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, int contactNumberId,
        NotificationType notificationType, string? body,
        DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null, int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        NotificationType = notificationType;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAccepted(string providerMessageSid, string? providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        AcceptedByProvider = true;
    }

    public void MarkRejected(string? providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        AcceptedByProvider = false;
    }

    public void UpdateProviderStatus(string? status, int? errorCode, string? errorMessage)
    {
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
    }
}
