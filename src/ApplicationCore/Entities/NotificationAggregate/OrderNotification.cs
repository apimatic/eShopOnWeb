using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS message sent (or attempted) about an order as it moves through its lifecycle.
///
/// A notification keeps enough of the state the provider owns — its message identifier
/// (<see cref="ProviderMessageId"/>) and current delivery outcome (<see cref="Status"/>) — that a
/// later request can act on it (cancel a scheduled follow-up, resend, dispose of the content) and
/// report on it, not only the request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, NotificationType type, string toPhoneNumber, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToPhoneNumber = toPhoneNumber;
        Body = body;
        Status = NotificationStatus.Pending;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>Owner of the order — used to scope shopper-facing reads to their own data.</summary>
    public string BuyerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Provider-canonical E.164 destination (PII — never written to logs).</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own identifier for this message (e.g. an <c>SM…</c> SID). Null if the hand-off failed.</summary>
    public string? ProviderMessageId { get; private set; }

    /// <summary>The provider's current delivery outcome, or a local <see cref="NotificationStatus"/> value.</summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>When a follow-up is queued with the provider to be sent later; null for immediate messages.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>Caller-supplied key that makes a resend idempotent. Null for messages not produced by a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this message was produced by resending an earlier one, the earlier one's id.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>True once the message content has been disposed of at the shopper's request.</summary>
    public bool ContentDisposed { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset? UpdatedDate { get; private set; }

    /// <summary>Records that this message was created as a scheduled (future) send.</summary>
    public void MarkScheduled(DateTimeOffset scheduledSendAt)
    {
        ScheduledSendAt = scheduledSendAt;
        Touch();
    }

    /// <summary>Marks this message as the product of resending <paramref name="originalNotificationId"/> under <paramref name="idempotencyKey"/>.</summary>
    public void MarkResendOf(int originalNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
        Touch();
    }

    /// <summary>Records the provider's acceptance of the message: its id and the initial status it reported.</summary>
    public void RecordProviderResult(string providerMessageId, string status, int? errorCode = null, string? errorMessage = null)
    {
        Guard.Against.NullOrEmpty(providerMessageId, nameof(providerMessageId));
        ProviderMessageId = providerMessageId;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Records that handing the message to the provider failed. The order operation is unaffected.</summary>
    public void RecordSendFailure(string? errorMessage)
    {
        Status = NotificationStatus.SendFailed;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Refreshes the delivery outcome from a later provider read.</summary>
    public void UpdateDeliveryStatus(string status, int? errorCode, string? errorMessage)
    {
        if (string.IsNullOrEmpty(status))
            return;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>
    /// Disposes of the message content locally. The caller is responsible for redacting the body at
    /// the provider first; the record of the send and its outcome deliberately survive.
    /// </summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
        Touch();
    }

    private void Touch() => UpdatedDate = DateTimeOffset.UtcNow;
}
