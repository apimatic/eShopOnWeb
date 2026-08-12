using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of one message the shop raised about an order, and what became of it. It carries
/// enough of the state the provider owns — the message identifier (<see cref="ProviderMessageSid"/>)
/// and its current delivery outcome (<see cref="Status"/>) — that a later request can act on it
/// (resend, dispose of content, cancel a scheduled follow-up) and report on it, not only the
/// request that first sent it.
///
/// The message body text is deliberately never stored here: the provider holds it, and disposing
/// of content means removing it there. What survives locally is the fact a message was sent and
/// what became of it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }

    public Notification(string buyerId, int orderId, NotificationKind kind, string toNumber)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        BuyerId = buyerId;
        OrderId = orderId;
        Kind = kind;
        ToNumber = toNumber;
        Status = MessageDeliveryStatus.NotSent;
    }

    /// <summary>Owner of the order this notification is about (the JWT name claim).</summary>
    public string BuyerId { get; private set; }

    public int OrderId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The destination number. Held so a resend can reach the same handset. Never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The provider's identifier for the message (e.g. an <c>SM…</c> SID). Null until accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome for the message. See <see cref="MessageDeliveryStatus"/>.</summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>When a scheduled follow-up is timed to go out, if this is one.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>True once the message content has been disposed of at the provider.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>
    /// The idempotency key that produced this notification, when it was created by an operator
    /// resend. Lets a repeat of the same resend request return this record instead of sending again.
    /// </summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this notification is a resend, the notification it was a resend of.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>True when this notification was created by an operator resending an earlier message.</summary>
    public bool IsResend => ResendOfNotificationId.HasValue;

    /// <summary>Records the provider's acceptance of the message: its SID and initial status.</summary>
    public void RecordProviderResult(string? sid, string status, int? errorCode, string? errorMessage, DateTimeOffset? scheduledFor = null)
    {
        ProviderMessageSid = sid;
        Status = string.IsNullOrEmpty(status) ? MessageDeliveryStatus.NotSent : status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        if (scheduledFor.HasValue)
        {
            ScheduledFor = scheduledFor;
        }
    }

    /// <summary>Advances the delivery outcome after re-reading it from the provider.</summary>
    public void UpdateDeliveryOutcome(string status, int? errorCode, string? errorMessage)
    {
        if (string.IsNullOrEmpty(status))
        {
            return;
        }
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void MarkContentDisposed() => ContentDisposed = true;

    /// <summary>Marks the idempotency and provenance of a resend.</summary>
    public void MarkAsResend(int originalNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
    }
}
