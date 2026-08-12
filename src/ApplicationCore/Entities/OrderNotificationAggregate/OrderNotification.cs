using System;
using Ardalis.GuardClauses;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// eShop's record of one SMS the shop asked the provider to deliver for an order.
/// <para>
/// It deliberately does NOT keep a local copy of the message text — the body lives
/// with the provider, so that disposing of the content (redacting it at the provider)
/// leaves nothing retrievable anywhere. What is kept here is the state the provider
/// owns: the provider's message identifier and the last known delivery outcome, plus
/// enough context (order, type, destination) for a later request to act on the message
/// and report on it.
/// </para>
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    /// <summary>Local status used before the provider has accepted the message.</summary>
    public const string PendingStatus = "pending";

    /// <summary>Local status used when the provider never accepted the request (e.g. a transport error).</summary>
    public const string SendFailedStatus = "send_failed";

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, NotificationType type, string toNumber)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToNumber = toNumber;
        Status = PendingStatus;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>The owning shopper (username / Order.BuyerId), used for shopper-scoped access checks.</summary>
    public string BuyerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>The canonical destination number. Persisted for resend/redaction; never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The provider's message identifier (SID). Null only when the provider never accepted the request.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The last known provider delivery status (e.g. queued, sent, delivered, undelivered, failed, scheduled, canceled).</summary>
    public string Status { get; private set; }

    /// <summary>The provider's error code when the message failed or was undelivered; otherwise null.</summary>
    public int? ErrorCode { get; private set; }

    /// <summary>The sender the provider actually used, recorded from the provider's response (used by reconciliation).</summary>
    public string? SentFrom { get; private set; }

    /// <summary>When the provider reports the message was sent.</summary>
    public DateTimeOffset? ProviderDateSent { get; private set; }

    /// <summary>True for the delivery follow-up that was queued with the provider to go out later.</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>True once the message content has been disposed of (redacted) at the provider.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this notification via a resend, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public DateTimeOffset? UpdatedDate { get; private set; }

    /// <summary>Record that the provider accepted the message, capturing the state it now owns.</summary>
    public void RecordAccepted(string providerMessageSid, string status, string? sentFrom, int? errorCode,
        DateTimeOffset? providerDateSent, bool isScheduled)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = string.IsNullOrEmpty(status) ? PendingStatus : status;
        SentFrom = sentFrom;
        ErrorCode = errorCode;
        ProviderDateSent = providerDateSent;
        IsScheduled = isScheduled;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Record that the provider never accepted the request. The order operation still succeeds.</summary>
    public void RecordSendFailure(int? errorCode)
    {
        Status = SendFailedStatus;
        ErrorCode = errorCode;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Advance the stored delivery outcome from a fresh read of the provider's record.</summary>
    public void RefreshDeliveryState(string status, int? errorCode, string? sentFrom, DateTimeOffset? providerDateSent)
    {
        if (string.IsNullOrEmpty(status))
        {
            return;
        }
        Status = status;
        ErrorCode = errorCode;
        if (!string.IsNullOrEmpty(sentFrom))
        {
            SentFrom = sentFrom;
        }
        if (providerDateSent.HasValue)
        {
            ProviderDateSent = providerDateSent;
        }
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Mark a scheduled follow-up as canceled after it was called off at the provider.</summary>
    public void MarkCanceled()
    {
        Status = "canceled";
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Mark that the content was disposed of at the provider. The record of the send survives.</summary>
    public void MarkContentDisposed()
    {
        ContentDisposed = true;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    public void AssignIdempotencyKey(string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        IdempotencyKey = idempotencyKey;
    }
}
