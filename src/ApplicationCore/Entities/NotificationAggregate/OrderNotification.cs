using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or tried to send) about an order. The record carries enough of the
/// state the provider owns — its message identifier (<see cref="ProviderMessageSid"/>) and last-known
/// delivery outcome (<see cref="DeliveryStatus"/>) — that a later request can act on it and report on
/// it, not only the request that created it. The <see cref="Id"/> is the public <c>notificationId</c>.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, NotificationType type, int contactNumberId, string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(contactNumberId, nameof(contactNumberId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ContactNumberId = contactNumberId;
        MessageBody = body;   // the intended text, kept so a failed message can be re-sent
        CreatedDate = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>The contact number this message targeted (used to resolve/redact later).</summary>
    public int ContactNumberId { get; private set; }

    /// <summary>Twilio message SID; null until the provider accepted the create.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Last-known provider delivery status (queued/sent/delivered/failed/undelivered/scheduled/canceled/…).</summary>
    public string? DeliveryStatus { get; private set; }

    /// <summary>The app's copy of the sent text. Nulled when the content is disposed of.</summary>
    public string? MessageBody { get; private set; }

    /// <summary>True once the message text has been redacted at the provider and locally.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>True for the delivery-survey message scheduled with the provider for later.</summary>
    public bool IsScheduledFollowUp { get; private set; }

    /// <summary>True once a still-pending scheduled follow-up has been called off with the provider.</summary>
    public bool FollowUpCanceled { get; private set; }

    /// <summary>True when the provider send failed; the underlying order operation still succeeds.</summary>
    public bool SendFailed { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>Caller-supplied idempotency key when this notification was produced by an operator resend.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The notification this one was a resend of, if any.</summary>
    public int? ResentFromNotificationId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public void MarkScheduledFollowUp() => IsScheduledFollowUp = true;

    public void MarkResendOf(int originalNotificationId, string idempotencyKey)
    {
        ResentFromNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>Record a successful provider create: store the SID and initial status.</summary>
    public void MarkSent(string providerMessageSid, string? deliveryStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = deliveryStatus;
        SendFailed = false;
        FailureReason = null;
    }

    /// <summary>Record a send that could not be created at the provider — never fatal to the order op.</summary>
    public void MarkFailed(string reason)
    {
        SendFailed = true;
        FailureReason = reason;
    }

    /// <summary>Refresh the mirror of the provider's current delivery outcome.</summary>
    public void UpdateDeliveryStatus(string? deliveryStatus)
    {
        if (!string.IsNullOrEmpty(deliveryStatus))
            DeliveryStatus = deliveryStatus;
    }

    public void MarkFollowUpCanceled(string? deliveryStatus)
    {
        FollowUpCanceled = true;
        if (!string.IsNullOrEmpty(deliveryStatus))
            DeliveryStatus = deliveryStatus;
    }

    /// <summary>Dispose of the message content locally after it has been redacted at the provider.</summary>
    public void MarkContentDisposed()
    {
        MessageBody = null;
        ContentRedacted = true;
    }
}
