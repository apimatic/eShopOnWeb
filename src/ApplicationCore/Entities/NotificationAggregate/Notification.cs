using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or scheduled) to a shopper about one of their orders, together with
/// the slice of state the messaging provider owns for it: the provider's message identifier and the
/// current delivery outcome. That is deliberately persisted so a <em>later</em> request — a resend,
/// a content disposal, a status read, a reconciliation — can act on and report about the message,
/// not only the request that first sent it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>The shopper this message is about; used to keep one shopper's data away from another.</summary>
    public string BuyerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Destination in E.164 form. Sensitive: this is never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>
    /// The message text. Nulled out once the content has been disposed of (redacted at the provider).
    /// The record itself — that a message was sent and what became of it — survives disposal.
    /// </summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for this message (Twilio Message SID). Null if the send call itself failed.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome, stored verbatim (queued, sent, delivered, undelivered, failed, scheduled, canceled...).</summary>
    public string? ProviderStatus { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>When a scheduled (follow-up) message is due to go out. Null for immediate messages.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this message via a resend, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>True once the message content has been disposed of at the provider and locally.</summary>
    public bool ContentRedacted { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    private Notification(int orderId, string buyerId, NotificationType type, string toNumber, string body,
        DateTimeOffset? scheduledSendAt, string? idempotencyKey)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToNumber = toNumber;
        Body = body;
        ScheduledSendAt = scheduledSendAt;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>An immediate message that is about to be sent to the provider.</summary>
    public static Notification ForImmediate(int orderId, string buyerId, NotificationType type, string toNumber,
        string body, string? idempotencyKey = null)
        => new(orderId, buyerId, type, toNumber, body, scheduledSendAt: null, idempotencyKey);

    /// <summary>A message to be scheduled with the provider for a future <paramref name="sendAt"/>.</summary>
    public static Notification ForScheduled(int orderId, string buyerId, NotificationType type, string toNumber,
        string body, DateTimeOffset sendAt)
        => new(orderId, buyerId, type, toNumber, body, sendAt, idempotencyKey: null);

    /// <summary>Records that the provider accepted the message, capturing its identifier and initial status.</summary>
    public void RecordProviderResult(string? providerMessageSid, string? providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Records that the message could not be handed to the provider at all (network/HTTP failure).</summary>
    public void RecordSendFailure(string errorMessage)
    {
        ProviderStatus = SendFailedStatus;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Refreshes the delivery outcome from a later read of the provider's record.</summary>
    public void UpdateDeliveryState(string? providerStatus, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(providerStatus))
            ProviderStatus = providerStatus;
        if (errorCode.HasValue)
            ErrorCode = errorCode;
        if (!string.IsNullOrEmpty(errorMessage))
            ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Disposes of the message content locally after it has been redacted at the provider.</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        Touch();
    }

    /// <summary>True when the provider's record shows this message actually reached the handset.</summary>
    public bool ReachedRecipient =>
        string.Equals(ProviderStatus, "delivered", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(ProviderStatus, "read", StringComparison.OrdinalIgnoreCase);

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    /// <summary>Local sentinel used when the provider never accepted the message, so it is distinguishable from a provider status.</summary>
    public const string SendFailedStatus = "send_failed";
}
