using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single outbound SMS raised for an order. It records enough of the state the provider owns
/// (the provider message identifier and the current delivery outcome) that a later request can
/// act on the message and report on it — not only the request that sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    /// <summary>Local sentinel status used when the provider never accepted the send (no SID was issued).</summary>
    public const string SendFailedStatus = "send_failed";

    /// <summary>Local status used before a send has been attempted.</summary>
    public const string PendingStatus = "pending";

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string ownerId, NotificationKind kind, string toNumber, string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        OwnerId = ownerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        DeliveryStatus = PendingStatus;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }

    /// <summary>The buyer the order (and therefore this notification) belongs to. Used for shopper scoping.</summary>
    public string OwnerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number in E.164. Treated as PII: never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Nulled out once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for this message (a message SID). Null until the provider accepts the send.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome for this message (its status verbatim, e.g. queued/sent/delivered/undelivered/scheduled/canceled).</summary>
    public string DeliveryStatus { get; private set; }

    public int? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>When set, this message is queued with the provider to be sent at this time (the delayed follow-up).</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>True once the message content has been disposed of (redacted at the provider and cleared here).</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>The caller-supplied idempotency key, when this notification was produced by an operator re-send.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The notification this one re-sent, when produced by an operator re-send.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>True when the message is a candidate for reconciliation and status refresh (the provider has a SID for it).</summary>
    public bool HasProviderMessage => !string.IsNullOrEmpty(ProviderMessageSid);

    /// <summary>The provider considers these delivery outcomes final; no point refreshing them.</summary>
    public bool IsTerminal =>
        DeliveryStatus is "delivered" or "undelivered" or "failed" or "canceled" or "read" or SendFailedStatus;

    /// <summary>Records the provider's acceptance of a send (immediate or scheduled).</summary>
    public void RecordAccepted(string providerMessageSid, string status, int? errorCode, string? errorMessage, DateTimeOffset? scheduledFor)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = string.IsNullOrEmpty(status) ? DeliveryStatus : status;
        ErrorCode = errorCode;
        ErrorMessage = Sanitize(errorMessage);
        ScheduledFor = scheduledFor;
        Touch();
    }

    /// <summary>Records that the provider never accepted the send. This must never fail the underlying order operation.</summary>
    public void RecordSendFailure()
    {
        DeliveryStatus = SendFailedStatus;
        Touch();
    }

    /// <summary>Refreshes the delivery outcome from a later read of the provider's record.</summary>
    public void UpdateDeliveryState(string status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
        {
            DeliveryStatus = status;
        }
        ErrorCode = errorCode;
        ErrorMessage = Sanitize(errorMessage);
        Touch();
    }

    /// <summary>Marks a scheduled message as cancelled after the provider confirmed the cancellation.</summary>
    public void MarkCancelled()
    {
        DeliveryStatus = "canceled";
        Touch();
    }

    /// <summary>Disposes of the message content: the provider text has been redacted and the local copy is cleared. The record itself survives.</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
        Touch();
    }

    /// <summary>Builds a fresh notification that re-sends this one, under a caller-supplied idempotency key.</summary>
    public OrderNotification CreateResend(string idempotencyKey, string body)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        return new OrderNotification(OrderId, OwnerId, Kind, ToNumber, body)
        {
            IdempotencyKey = idempotencyKey,
            ResendOfNotificationId = Id
        };
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;

    /// <summary>
    /// Removes any occurrence of the destination number from provider-supplied text before it is stored,
    /// so the shopper's number can never leak back out through an error message.
    /// </summary>
    private string? Sanitize(string? message)
    {
        if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(ToNumber))
        {
            return message;
        }
        return message.Replace(ToNumber, "[redacted]", StringComparison.Ordinal);
    }
}
