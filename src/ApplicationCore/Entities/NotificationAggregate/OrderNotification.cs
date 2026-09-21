using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single text message eShop raised about an order, together with enough of the provider-owned state
/// (its message identifier and current delivery outcome) that a later request can act on it and report on
/// it. Belongs to the shopper who owns the order. The destination number and message body are stored so the
/// message can be re-sent or disposed of, but are never written to logs.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        string buyerId,
        int orderId,
        NotificationType type,
        string toNumberE164,
        string body,
        bool isScheduledFollowUp,
        string? resendIdempotencyKey = null,
        int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumberE164, nameof(toNumberE164));
        Guard.Against.NullOrEmpty(body, nameof(body));

        BuyerId = buyerId;
        OrderId = orderId;
        Type = type;
        ToNumberE164 = toNumberE164;
        Body = body;
        IsScheduledFollowUp = isScheduledFollowUp;
        ResendIdempotencyKey = resendIdempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
        DeliveryState = NotificationDeliveryState.NotSent;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The owning shopper (the username carried on the JWT).</summary>
    public string BuyerId { get; private set; }

    public int OrderId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Destination in E.164 form. Never logged.</summary>
    public string ToNumberE164 { get; private set; }

    /// <summary>Message text. Cleared once a shopper asks for the content to be disposed of. Never logged.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own message identifier (Twilio Sid), once a message resource exists.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's raw delivery status string (e.g. queued, sent, delivered, undelivered).</summary>
    public string? ProviderStatus { get; private set; }

    /// <summary>eShop's coarse, actionable view of the delivery outcome.</summary>
    public NotificationDeliveryState DeliveryState { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>True when this notification is the scheduled follow-up that can be cancelled before it sends.</summary>
    public bool IsScheduledFollowUp { get; private set; }

    /// <summary>True once the message body has been disposed of at the provider and cleared locally.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>The provider's event time (when the message was sent), used as the reconciliation clock.</summary>
    public DateTimeOffset? ProviderDateSent { get; private set; }

    /// <summary>Caller-supplied idempotency key that produced this notification via a resend. Unique when set.</summary>
    public string? ResendIdempotencyKey { get; private set; }

    /// <summary>The notification this one was a resend of, when applicable.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>A message that did not reach the shopper is eligible to be re-sent.</summary>
    public bool CanBeResent => DeliveryState == NotificationDeliveryState.Failed;

    /// <summary>Record the outcome of a completed provider send/schedule call.</summary>
    public void RecordProviderResult(
        string? providerMessageSid,
        string? providerStatus,
        NotificationDeliveryState deliveryState,
        int? providerErrorCode,
        string? providerErrorMessage,
        DateTimeOffset? providerDateSent)
    {
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        DeliveryState = deliveryState;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
        if (providerDateSent.HasValue)
            ProviderDateSent = providerDateSent;
    }

    /// <summary>Record a send whose transport failed after the request may have been received.</summary>
    public void RecordSendUnknown(string? providerErrorMessage)
    {
        DeliveryState = NotificationDeliveryState.Unknown;
        ProviderErrorMessage = providerErrorMessage;
    }

    /// <summary>Record that a scheduled follow-up was cancelled before it went out.</summary>
    public void MarkCancelled(string? providerStatus)
    {
        DeliveryState = NotificationDeliveryState.Cancelled;
        if (!string.IsNullOrEmpty(providerStatus))
            ProviderStatus = providerStatus;
    }

    /// <summary>Refresh the delivery outcome from a later read of the provider's message resource.</summary>
    public void RefreshDelivery(
        string? providerStatus,
        NotificationDeliveryState deliveryState,
        int? providerErrorCode,
        string? providerErrorMessage,
        DateTimeOffset? providerDateSent)
    {
        ProviderStatus = providerStatus;
        DeliveryState = deliveryState;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
        if (providerDateSent.HasValue)
            ProviderDateSent = providerDateSent;
    }

    /// <summary>Mark the content disposed of locally once it has also been disposed at the provider.</summary>
    public void MarkContentDisposed()
    {
        ContentDisposed = true;
        Body = null;
    }
}
