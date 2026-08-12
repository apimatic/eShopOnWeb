using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of one SMS the shop asked the provider to send about an order, plus enough of the
/// state the provider owns (its message identifier and current delivery outcome) that a later
/// request can act on it and report on it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    // The order this message is about.
    public int OrderId { get; private set; }

    // The shopper who owns the order/number. Denormalised so notifications can be scoped to a
    // caller without loading the order.
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    // The contact number this message targets. Resend is gated on this number still existing:
    // a number the shopper has deleted must never be messaged again.
    public int ContactNumberId { get; private set; }

    // Destination E.164 at send time. Held for reconciliation against the provider's own record
    // and never written to logs.
    public string ToNumber { get; private set; }

    // Provider message identifier (Twilio message SID). Null only when the send request never
    // produced a message.
    public string? ProviderMessageSid { get; private set; }

    // Current delivery outcome as owned by the provider (see NotificationStatus).
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    // The text that was sent. Null once the content has been disposed of at the shopper's request.
    public string? Body { get; private set; }

    // True once the message content has been redacted at the provider (and here).
    public bool ContentRedacted { get; private set; }

    // When set (DeliveryFollowUp), the time the provider was asked to send the message.
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    // Caller-supplied idempotency key for a resend that produced this notification.
    public string? IdempotencyKey { get; private set; }

    // When this notification was produced by resending an earlier one, its id.
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    public Notification(int orderId, string buyerId, NotificationKind kind, int contactNumberId,
        string toNumber, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ContactNumberId = contactNumberId;
        ToNumber = toNumber;
        Body = body;
        Status = NotificationStatus.SendFailed; // until a provider result is recorded
    }

    /// <summary>Records a successful hand-off to the provider (or a scheduled message).</summary>
    public void RecordProviderResult(string providerMessageSid, string status, int? errorCode, string? errorMessage,
        DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = string.IsNullOrEmpty(status) ? NotificationStatus.Queued : status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        if (scheduledSendAt.HasValue)
        {
            ScheduledSendAt = scheduledSendAt;
        }
    }

    /// <summary>Records that the send request itself failed before the provider issued an identifier.</summary>
    public void RecordSendFailure(string? errorMessage)
    {
        Status = NotificationStatus.SendFailed;
        ErrorMessage = errorMessage;
    }

    /// <summary>Refreshes the delivery outcome from the provider's current view.</summary>
    public void UpdateDeliveryState(string status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
        {
            Status = status;
        }
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Marks the content disposed of. The record of the message and its outcome survives.</summary>
    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
    }

    public void MarkAsResendOf(int originalNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
    }
}
