using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or scheduled) about an order. It carries enough of the state the
/// provider owns — the provider message identifier and the last-known delivery outcome — that a
/// later request can act on it (resend, redact, cancel a scheduled send) and report on it, not
/// only the request that created it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    /// <summary>Local status used when the provider never accepted the message (no SID yet).</summary>
    public const string NotSentStatus = "not_sent";

    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order this message is about.</summary>
    public string OwnerId { get; private set; }

    /// <summary>Destination number (E.164). Persisted for resend/scheduling; never logged.</summary>
    public string ToPhoneNumber { get; private set; }

    public OrderNotificationType Type { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of (redacted).</summary>
    public string? Body { get; private set; }

    public bool ContentRedacted { get; private set; }

    /// <summary>The provider's identifier for this message (Twilio Message SID, <c>SM…</c>).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (queued, sent, delivered, failed, …).</summary>
    public string? ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>When the provider accepted the send (for immediate messages).</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>When a scheduled follow-up is due to be sent by the provider.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True for a message queued with the provider for a future <see cref="ScheduledSendAt"/>.</summary>
    public bool IsScheduled { get; private set; }

    public DateTimeOffset? ProviderStatusUpdatedAt { get; private set; }

    /// <summary>Caller-supplied idempotency key for a resend; null for the original message.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Set when this notification was produced by resending an earlier one.</summary>
    public int? ResendOfNotificationId { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string ownerId, string toPhoneNumber, OrderNotificationType type, string body)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));

        OrderId = orderId;
        OwnerId = ownerId;
        ToPhoneNumber = toPhoneNumber;
        Type = type;
        Body = body;
    }

    /// <summary>Record that the provider accepted an immediate send.</summary>
    public void RecordSent(string providerMessageSid, string providerStatus, int? errorCode)
    {
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        SentAt = DateTimeOffset.UtcNow;
        ProviderStatusUpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Record that the provider accepted a scheduled send for a future time.</summary>
    public void RecordScheduled(string providerMessageSid, string providerStatus, DateTimeOffset scheduledSendAt)
    {
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        IsScheduled = true;
        ScheduledSendAt = scheduledSendAt;
        SentAt = DateTimeOffset.UtcNow; // when it was queued with the provider
        ProviderStatusUpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Record that handing the message to the provider failed. The underlying order
    /// operation still succeeds; this simply captures that nothing went out.</summary>
    public void RecordNotSent()
    {
        ProviderStatus = NotSentStatus;
        ProviderStatusUpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Refresh the last-known provider delivery outcome.</summary>
    public void UpdateProviderStatus(string? providerStatus, int? errorCode)
    {
        if (providerStatus is null) return;
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderStatusUpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Dispose of the message text locally. The provider-side redaction is done by the
    /// caller; here we drop the stored copy while keeping the send record.</summary>
    public void Redact()
    {
        Body = null;
        ContentRedacted = true;
    }

    public void SetIdempotencyKey(string idempotencyKey) => IdempotencyKey = idempotencyKey;

    public void SetResendOf(int notificationId) => ResendOfNotificationId = notificationId;
}
