using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop raised about an order, plus the state the provider owns for it (its message
/// identifier and current delivery outcome) so a later request can act on it and report on it.
/// The row is created BEFORE the provider call (status <see cref="NotificationDeliveryStatus.Pending"/>,
/// no SID); the provider's result is written back afterwards.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string ownerId, NotificationKind kind, string toNumber,
        string content, bool isScheduled = false, int? resendOfNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.Null(content, nameof(content));

        OrderId = orderId;
        OwnerId = ownerId;
        Kind = kind;
        ToNumber = toNumber;
        Content = content;
        IsScheduled = isScheduled;
        ResendOfNotificationId = resendOfNotificationId;
    }

    public int OrderId { get; private set; }

    /// <summary>The shopper the message is about (JWT name claim). Scopes shopper reads.</summary>
    public string OwnerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number (canonical E.164). Sensitive — never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Null once its content has been disposed of. Sensitive — never logged.</summary>
    public string? Content { get; private set; }

    public bool ContentRedacted { get; private set; }

    /// <summary>Whether this is a future-dated (scheduled) message queued with the provider.</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>For a resend, the notification whose message this re-sends.</summary>
    public int? ResendOfNotificationId { get; private set; }

    // --- Provider-owned state ---

    /// <summary>The provider's message identifier (Twilio SID). Null until a send returns.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's raw status text (e.g. "queued", "delivered"). Empty until known.</summary>
    public string ProviderStatusRaw { get; private set; } = string.Empty;

    public NotificationDeliveryStatus DeliveryStatus { get; private set; } = NotificationDeliveryStatus.Pending;

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>The provider's own send timestamp — the clock reconciliation filters on.</summary>
    public DateTimeOffset? ProviderSentAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Records a successful hand-off to the provider (immediate or scheduled).</summary>
    public void RecordSent(string providerMessageSid, NotificationDeliveryStatus deliveryStatus,
        string providerStatusRaw, int? errorCode, string? errorMessage, DateTimeOffset? providerSentAt)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ApplyStatus(deliveryStatus, providerStatusRaw, errorCode, errorMessage, providerSentAt);
    }

    /// <summary>Records that the send transport failed after the request may have been received.</summary>
    public void RecordSendUnknown()
    {
        DeliveryStatus = NotificationDeliveryStatus.Unknown;
        ProviderStatusRaw = "unknown";
    }

    /// <summary>Updates the stored outcome from a fresh read of the provider's state.</summary>
    public void RefreshStatus(NotificationDeliveryStatus deliveryStatus, string providerStatusRaw,
        int? errorCode, string? errorMessage, DateTimeOffset? providerSentAt)
    {
        ApplyStatus(deliveryStatus, providerStatusRaw, errorCode, errorMessage, providerSentAt);
    }

    /// <summary>Marks a not-yet-sent scheduled message as called off.</summary>
    public void MarkScheduledCancelled()
    {
        DeliveryStatus = NotificationDeliveryStatus.Cancelled;
        ProviderStatusRaw = "canceled";
    }

    /// <summary>Disposes of the message content locally; the provider copy is redacted separately.</summary>
    public void RedactContent()
    {
        Content = null;
        ContentRedacted = true;
    }

    /// <summary>Whether the message did not reach the shopper and may be re-sent.</summary>
    public bool IsResendable => DeliveryStatus == NotificationDeliveryStatus.Failed;

    private void ApplyStatus(NotificationDeliveryStatus deliveryStatus, string providerStatusRaw,
        int? errorCode, string? errorMessage, DateTimeOffset? providerSentAt)
    {
        DeliveryStatus = deliveryStatus;
        ProviderStatusRaw = providerStatusRaw ?? string.Empty;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (providerSentAt.HasValue)
        {
            ProviderSentAt = providerSentAt;
        }
    }
}
