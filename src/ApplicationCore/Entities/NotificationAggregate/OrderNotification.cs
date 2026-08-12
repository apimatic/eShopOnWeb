using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS raised for an order. Carries enough of the state the provider owns
/// — its message identifier (<see cref="ProviderMessageSid"/>) and current delivery
/// outcome (<see cref="DeliveryStatus"/>) — that a later request can act on it
/// (fetch fresh status, cancel a scheduled send, redact the body, resend) and report
/// on it, not merely the request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    /// <summary>
    /// Creates a notification record before the provider call is attempted. The record
    /// exists even if the send later fails, so an operator can see it and resend.
    /// </summary>
    public OrderNotification(int orderId, string ownerId, NotificationType type, string toNumber, string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.Null(body, nameof(body));

        OrderId = orderId;
        OwnerId = ownerId;
        Type = type;
        ToNumber = toNumber;
        Body = body;
        DeliveryStatus = MessageDeliveryStatus.Queued;
        CreatedDate = DateTimeOffset.UtcNow;
        UpdatedDate = CreatedDate;
    }

    /// <summary>The order this message concerns.</summary>
    public int OrderId { get; private set; }

    /// <summary>Owner (shopper) of the order — used to scope shopper-facing reads.</summary>
    public string OwnerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Destination E.164 number. Persisted for resend/reconciliation; never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>Message text. Cleared locally once the shopper asks for it to be disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>Provider message identifier (Twilio SID). Null if the provider never accepted the message.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Provider's current delivery outcome, stored verbatim (see <see cref="MessageDeliveryStatus"/>).</summary>
    public string DeliveryStatus { get; private set; }

    /// <summary>Provider error code for a failed/undelivered message, if any.</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>Provider error description for a failed/undelivered message, if any.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>True for the dispatch follow-up that is queued with the provider for a future time.</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>When a scheduled message is due to be sent by the provider.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message body has been redacted at the provider and cleared locally.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key for a resend, so a repeat under the same key sends nothing new.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this notification was produced by a resend, the id of the notification it re-sent.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset UpdatedDate { get; private set; }

    /// <summary>Records a successful immediate send: the provider accepted the message and returned a sid + status.</summary>
    public void MarkSent(string providerMessageSid, string deliveryStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = deliveryStatus;
        IsScheduled = false;
        Touch();
    }

    /// <summary>Records that the message was scheduled with the provider for a future time.</summary>
    public void MarkScheduled(string providerMessageSid, DateTimeOffset sendAt, string deliveryStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = deliveryStatus;
        IsScheduled = true;
        ScheduledSendAt = sendAt;
        Touch();
    }

    /// <summary>Records that the provider call failed before a message id was obtained.</summary>
    public void MarkSendFailed(string? reason)
    {
        DeliveryStatus = MessageDeliveryStatus.Failed;
        ErrorMessage = reason;
        Touch();
    }

    /// <summary>Refreshes the delivery outcome from the provider's current view of the message.</summary>
    public void UpdateDeliveryStatus(string deliveryStatus, string? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(deliveryStatus))
        {
            DeliveryStatus = deliveryStatus;
        }
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Records that a scheduled message was cancelled with the provider before it went out.</summary>
    public void MarkCanceled()
    {
        DeliveryStatus = MessageDeliveryStatus.Canceled;
        Touch();
    }

    /// <summary>Clears the local body after the provider copy has been redacted. Sid and status survive.</summary>
    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
        Touch();
    }

    public void SetIdempotencyKey(string idempotencyKey)
    {
        IdempotencyKey = idempotencyKey;
    }

    public void SetResendOf(int originalNotificationId)
    {
        ResendOfNotificationId = originalNotificationId;
    }

    private void Touch() => UpdatedDate = DateTimeOffset.UtcNow;
}
