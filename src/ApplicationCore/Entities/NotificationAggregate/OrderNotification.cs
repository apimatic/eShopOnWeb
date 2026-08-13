using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS message that eShop raised for an order, together with the state the provider owns
/// for it (its identifier and current delivery outcome). One <see cref="OrderNotification"/> is
/// created per destination per order event, so a later request can act on it and report on it —
/// not only the request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    private OrderNotification(string buyerId, int orderId, NotificationKind kind, string toNumber, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        BuyerId = buyerId;
        OrderId = orderId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        Status = NotificationStatus.PendingSend;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner of the underlying order — a shopper only ever sees their own notifications.</summary>
    public string BuyerId { get; private set; }

    public int OrderId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number (PII — never logged).</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Nulled out once the shopper asks for the content to be disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio Message SID). Null if the send never reached the provider.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome, mirrored verbatim. See <see cref="NotificationStatus"/>.</summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>True for the "how did delivery go?" follow-up that is queued with the provider for a later time.</summary>
    public bool IsScheduled { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message content has been disposed of at the provider and locally.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key that produced this notification (set only for operator re-sends).</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this notification is a re-send, the notification it was re-sent from.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>Set when the send attempt could not even reach the provider (kept out of the delivery outcome).</summary>
    public string? DispatchError { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastRefreshedAt { get; private set; }

    // ---- Factory helpers ----

    public static OrderNotification ForImmediate(string buyerId, int orderId, NotificationKind kind, string toNumber, string body)
        => new(buyerId, orderId, kind, toNumber, body);

    public static OrderNotification ForScheduled(string buyerId, int orderId, NotificationKind kind, string toNumber, string body, DateTimeOffset sendAt)
    {
        var n = new OrderNotification(buyerId, orderId, kind, toNumber, body)
        {
            IsScheduled = true,
            ScheduledSendAt = sendAt
        };
        return n;
    }

    public OrderNotification AsResendOf(OrderNotification source, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        ResendOfNotificationId = source.Id;
        IdempotencyKey = idempotencyKey;
        return this;
    }

    // ---- Behaviour ----

    /// <summary>Record the provider's acknowledgement of a create/schedule call.</summary>
    public void RecordProviderAccepted(string providerMessageSid, string? status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = string.IsNullOrEmpty(status) ? NotificationStatus.Queued : status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        DispatchError = null;
        LastRefreshedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Record that the send attempt could not reach the provider at all. This must never fail the
    /// underlying order operation — the notification simply carries the reason it did not go out.
    /// </summary>
    public void RecordDispatchFailure(string reason)
    {
        DispatchError = reason;
        Status = NotificationStatus.Failed;
        LastRefreshedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Refresh the delivery outcome from the provider's current view of the message.</summary>
    public void RefreshDeliveryOutcome(string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
        {
            Status = status;
        }
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        LastRefreshedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCanceled()
    {
        Status = NotificationStatus.Canceled;
        LastRefreshedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Dispose of the message content locally once it has also been disposed of at the provider.</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentRedacted = true;
        LastRefreshedAt = DateTimeOffset.UtcNow;
    }

    public bool IsCancelableFollowUp =>
        Kind == NotificationKind.DeliveryFeedback
        && IsScheduled
        && ProviderMessageSid is not null
        && !NotificationStatus.IsTerminal(Status);
}
