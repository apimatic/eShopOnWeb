using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of one SMS message the shop tried to send to one destination about one order.
///
/// It carries enough of the state the provider owns — the provider's message identifier
/// (<see cref="ProviderSid"/>) and the current delivery outcome (<see cref="DeliveryStatus"/>) — that a
/// later request can act on it (resend, redact, cancel) and report on it, not only the request that sent it.
///
/// A message that could not be handed to the provider at all is still recorded (with <see cref="SendFailed"/>
/// set) so the underlying order operation never fails because of a messaging problem.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }

    private Notification(int orderId, string buyerId, string recipient, NotificationType type, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(recipient, nameof(recipient));

        OrderId = orderId;
        BuyerId = buyerId;
        Recipient = recipient;
        Type = type;
        Body = body;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The order this notification is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>The owning shopper's identity (username) — carried directly so ownership can be checked
    /// without loading the order aggregate.</summary>
    public string BuyerId { get; private set; }

    /// <summary>The destination number (canonical E.164). A personal contact detail — never logged.</summary>
    public string Recipient { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of (see <see cref="DisposeContent"/>).</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own identifier for the message (Twilio message SID). Null only when the send
    /// call never reached the provider (<see cref="SendFailed"/>).</summary>
    public string? ProviderSid { get; private set; }

    /// <summary>The provider's current delivery outcome, stored as the provider's own status token
    /// (e.g. queued, sent, delivered, failed, undelivered, scheduled, canceled).</summary>
    public string? DeliveryStatus { get; private set; }

    /// <summary>Provider failure detail for a message the carrier refused, when the provider supplies it.</summary>
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>True when the message could not be handed to the provider at all (transport/API failure before
    /// a SID was assigned). The order operation still succeeds; this records that no message went out.</summary>
    public bool SendFailed { get; private set; }
    public string? FailureReason { get; private set; }

    /// <summary>True for a follow-up message queued with the provider to be sent later (delivery feedback).</summary>
    public bool IsScheduled { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once a disposal request has blanked the message content at the provider.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>The caller-supplied idempotency key of the resend that produced this notification, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset? LastUpdatedDate { get; private set; }

    /// <summary>Create a notification for an immediate (send-now) message, before the provider has been called.</summary>
    public static Notification CreateImmediate(int orderId, string buyerId, string recipient, NotificationType type, string body)
        => new(orderId, buyerId, recipient, type, body);

    /// <summary>Create a notification for a message scheduled with the provider for later delivery.</summary>
    public static Notification CreateScheduled(int orderId, string buyerId, string recipient, NotificationType type, string body, DateTimeOffset sendAt)
    {
        var n = new Notification(orderId, buyerId, recipient, type, body)
        {
            IsScheduled = true,
            ScheduledSendAt = sendAt
        };
        return n;
    }

    /// <summary>Record that the provider accepted the message: capture its SID and reported status.</summary>
    public void MarkAccepted(string providerSid, string? deliveryStatus)
    {
        Guard.Against.NullOrEmpty(providerSid, nameof(providerSid));
        ProviderSid = providerSid;
        DeliveryStatus = deliveryStatus;
        SendFailed = false;
        FailureReason = null;
        Touch();
    }

    /// <summary>Record that the message could not be handed to the provider (no SID assigned).</summary>
    public void MarkSendFailed(string reason)
    {
        SendFailed = true;
        FailureReason = reason;
        Touch();
    }

    /// <summary>Update the delivery outcome from a later status read.</summary>
    public void UpdateDeliveryStatus(string? deliveryStatus, int? errorCode, string? errorMessage)
    {
        DeliveryStatus = deliveryStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Mark a scheduled message as called off before it went out.</summary>
    public void MarkCanceled()
    {
        DeliveryStatus = "canceled";
        Touch();
    }

    /// <summary>Blank the stored content locally once it has been disposed of at the provider. The record that a
    /// message was sent, and what became of it, survives.</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
        Touch();
    }

    /// <summary>Stamp this notification as the product of a resend under the given idempotency key.</summary>
    public void MarkResend(string idempotencyKey)
    {
        IdempotencyKey = idempotencyKey;
        Touch();
    }

    private void Touch() => LastUpdatedDate = DateTimeOffset.UtcNow;
}
