using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

/// <summary>
/// The record of one SMS message about an order: what it was, who it was for, the provider's identifier for
/// it, and what became of it. It carries enough of the state the provider owns (its message identifier and
/// current delivery outcome) that a later request can act on it (resend, cancel, redact) and report on it —
/// not only the request that first sent it.
///
/// A notification belongs to the order's shopper (<see cref="BuyerId"/>). The destination number is stored
/// here so the message can be resent, but it is never written to logs.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationType type,
        string toPhoneNumber,
        string body,
        DateTimeOffset? scheduledSendAt = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToPhoneNumber = toPhoneNumber;
        Body = body;
        ScheduledSendAt = scheduledSendAt;
        IdempotencyKey = idempotencyKey;
        Status = NotificationStatuses.Pending;
        CreatedDate = DateTimeOffset.UtcNow;
        UpdatedDate = CreatedDate;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper that owns the order (and therefore this notification).</summary>
    public string BuyerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>The E.164 destination. Persisted for resend; never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (its SID), once the provider has accepted it.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The current delivery outcome — mostly the provider's own wire status verbatim.
    /// See <see cref="NotificationStatuses"/>.</summary>
    public string Status { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    /// <summary>Provider-supplied failure detail. May reference the destination number, so it is stored but
    /// never logged.</summary>
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>When set, the time the provider is scheduled to send this message.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message text has been redacted at the provider and disposed of here.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>The caller-supplied idempotency key of the resend that produced this notification, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset UpdatedDate { get; private set; }

    /// <summary>Records that the provider accepted the message, capturing its SID and current status.</summary>
    public void MarkAccepted(string providerMessageSid, string status)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderMessageSid = providerMessageSid;
        Status = status;
        ProviderErrorCode = null;
        ProviderErrorMessage = null;
        Touch();
    }

    /// <summary>Records that the message never reached the provider.</summary>
    public void MarkSendFailed(int? providerErrorCode, string? providerErrorMessage)
    {
        Status = NotificationStatuses.SendFailed;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
        Touch();
    }

    /// <summary>Refreshes the delivery outcome from the provider's current view.</summary>
    public void UpdateDeliveryStatus(string status, int? providerErrorCode, string? providerErrorMessage)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        Status = status;
        if (providerErrorCode.HasValue)
        {
            ProviderErrorCode = providerErrorCode;
        }
        if (!string.IsNullOrEmpty(providerErrorMessage))
        {
            ProviderErrorMessage = providerErrorMessage;
        }
        Touch();
    }

    /// <summary>Records that a scheduled message was called off at the provider.</summary>
    public void MarkCanceled()
    {
        Status = NotificationStatuses.Canceled;
        Touch();
    }

    /// <summary>Disposes of the message text locally. Call after redacting it at the provider.</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        Touch();
    }

    private void Touch() => UpdatedDate = DateTimeOffset.UtcNow;
}
