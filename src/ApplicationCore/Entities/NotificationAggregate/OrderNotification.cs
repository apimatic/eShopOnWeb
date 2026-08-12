using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop raised for an order. It carries enough of the state the
/// provider owns — the provider's message identifier (<see cref="ProviderMessageSid"/>)
/// and the current delivery outcome (<see cref="ProviderStatus"/>) — that a later
/// request can act on the message (fetch, cancel, redact, resend) and report on it,
/// not only the request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, NotificationKind kind, string toPhoneNumber, string? body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToPhoneNumber = toPhoneNumber;
        Body = body;
        ProviderStatus = NotificationStatuses.Pending;
    }

    public int OrderId { get; private set; }

    /// <summary>Owning shopper's identity — the order this message is about belongs to them.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number (canonical E.164). Persisted, but never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>Message text. Cleared locally when the content is disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own identifier for this message (Twilio message SID), once accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Last-known delivery outcome as reported by the provider (e.g. queued, sent, delivered, undelivered, failed, scheduled, canceled).</summary>
    public string ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>True for a follow-up queued with the provider to go out later (and therefore cancellable before it sends).</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>True once the message text has been disposed of (redacted at the provider and cleared here). The record itself survives.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>Caller-supplied idempotency key that produced this message via a resend, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>If this message was produced by resending another, the id of that original notification.</summary>
    public int? ResentFromNotificationId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>When the provider records the message as sent (its date_sent), once known.</summary>
    public DateTimeOffset? ProviderDateSent { get; private set; }

    /// <summary>Record the outcome of submitting the message to the provider.</summary>
    public void RecordProviderResult(string? sid, string status, int? errorCode, string? errorMessage,
        bool isScheduled, DateTimeOffset? dateSent)
    {
        ProviderMessageSid = sid;
        ProviderStatus = string.IsNullOrEmpty(status) ? NotificationStatuses.Unknown : status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        IsScheduled = isScheduled;
        ProviderDateSent = dateSent;
    }

    /// <summary>Record that submission to the provider failed outright (no message was created).</summary>
    public void RecordSubmissionFailure(string errorMessage, int? errorCode = null)
    {
        ProviderStatus = NotificationStatuses.SubmissionFailed;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Refresh the cached delivery outcome from a later provider read.</summary>
    public void UpdateDeliveryOutcome(string status, int? errorCode, string? errorMessage, DateTimeOffset? dateSent)
    {
        if (!string.IsNullOrEmpty(status))
        {
            ProviderStatus = status;
        }
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (dateSent.HasValue)
        {
            ProviderDateSent = dateSent;
        }
        if (!string.Equals(status, NotificationStatuses.Scheduled, StringComparison.OrdinalIgnoreCase))
        {
            IsScheduled = false;
        }
    }

    /// <summary>Mark the follow-up as cancelled with the provider before it went out.</summary>
    public void MarkCancelled(string status)
    {
        ProviderStatus = string.IsNullOrEmpty(status) ? NotificationStatuses.Canceled : status;
        IsScheduled = false;
    }

    /// <summary>Dispose of the message content: the text is cleared here after being redacted at the provider.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
    }

    /// <summary>Stamp the idempotency provenance of a resend-produced message.</summary>
    public void SetResendProvenance(string idempotencyKey, int resentFromNotificationId)
    {
        IdempotencyKey = idempotencyKey;
        ResentFromNotificationId = resentFromNotificationId;
    }
}
