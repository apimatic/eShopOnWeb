using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of one SMS the shop tried to send a shopper about an order. It carries enough of the
/// state the provider owns — the provider message id and the current delivery outcome — that a
/// later request (status refresh, reconciliation, resend, content disposal) can act on and report
/// about the message, not only the request that first sent it.
///
/// The destination number is stored so a resend can reach the same handset, but is never written
/// to logs. The message text is kept for the operator's view and is cleared when a shopper asks
/// for the content to be disposed of.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, NotificationKind kind, string toNumber, string body)
    {
        OrderId = orderId;
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Kind = kind;
        ToNumber = Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Body = body;
        Status = NotificationStatuses.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>Identity username of the shopper the message is for. Used to scope shopper access.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The destination number (canonical E.164). Never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The provider's message id (SID), once the provider has accepted the message. Null if the send was rejected.</summary>
    public string? MessageSid { get; private set; }

    /// <summary>The current outcome: a provider wire status once sent, or a local marker (see <see cref="NotificationStatuses"/>).</summary>
    public string Status { get; private set; }

    /// <summary>The provider error code for a failed/undelivered message, when known.</summary>
    public int? ErrorCode { get; private set; }

    /// <summary>A caller-safe reason a send or cancel failed, when applicable. Never contains provider secrets.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>The message text. Cleared (null) once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>True once the shopper has asked for the content of this message to be disposed of.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>True for a message queued with the provider to go out in the future (the delivery follow-up).</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>When a scheduled message is due to be sent by the provider.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>The caller-supplied idempotency key for a resend, so a repeat under the same key does not send again.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the provider was last asked for this message's current delivery outcome.</summary>
    public DateTimeOffset? LastSyncedAt { get; private set; }

    /// <summary>Records the outcome of an immediate send attempt.</summary>
    public void RecordSendOutcome(string? messageSid, string status, int? errorCode, string? failureReason)
    {
        MessageSid = messageSid;
        Status = status;
        ErrorCode = errorCode;
        FailureReason = failureReason;
    }

    /// <summary>Records that this message was queued with the provider for future delivery.</summary>
    public void RecordScheduled(string? messageSid, string status, DateTimeOffset scheduledFor, string? failureReason)
    {
        IsScheduled = true;
        ScheduledFor = scheduledFor;
        MessageSid = messageSid;
        Status = status;
        FailureReason = failureReason;
    }

    public void SetIdempotencyKey(string key) => IdempotencyKey = key;

    /// <summary>Updates the delivery outcome from a fresh read of the provider's record.</summary>
    public void SyncDeliveryState(string status, int? errorCode)
    {
        Status = status;
        if (errorCode.HasValue)
            ErrorCode = errorCode;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks a scheduled follow-up as cancelled after it was called off with the provider.</summary>
    public void MarkScheduledCancelled(string? failureReason = null)
    {
        Status = NotificationStatuses.Canceled;
        if (failureReason is not null)
            FailureReason = failureReason;
    }

    /// <summary>Clears the locally-held message text after the content has been disposed of at the provider.</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
