using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of a single SMS the shop sent (or tried to send) to a shopper about an order.
/// It carries enough of the provider's own state — the message identifier and the current
/// delivery outcome — for a later request to act on the message and to report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    private OrderNotification(int orderId, string ownerId, NotificationKind kind, string to, string body)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        OwnerId = Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        To = Guard.Against.NullOrEmpty(to, nameof(to));
        Body = Guard.Against.NullOrEmpty(body, nameof(body));
        Kind = kind;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Creates a notification for an immediate message about an order.</summary>
    public static OrderNotification ForImmediate(int orderId, string ownerId, NotificationKind kind, string to, string body)
        => new(orderId, ownerId, kind, to, body);

    /// <summary>Creates a notification for a message scheduled with the provider for a later time.</summary>
    public static OrderNotification ForScheduled(int orderId, string ownerId, NotificationKind kind, string to, string body, DateTimeOffset sendAt)
        => new(orderId, ownerId, kind, to, body) { IsFollowUp = true, ScheduledSendAt = sendAt };

    public int OrderId { get; private set; }

    /// <summary>Identity (JWT name) of the shopper the order belongs to.</summary>
    public string OwnerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number (E.164). Sensitive — never written to logs.</summary>
    public string To { get; private set; }

    /// <summary>The message text. Sensitive; cleared when the shopper asks for the content to be disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio message SID), once it has been created.</summary>
    public string? ProviderSid { get; private set; }

    /// <summary>The number the provider recorded the message as sent from. Used for reconciliation.</summary>
    public string? ProviderFrom { get; private set; }

    /// <summary>The provider's current delivery outcome for the message (its raw status value).</summary>
    public string? DeliveryStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>When the provider recorded the message as sent, when known.</summary>
    public DateTimeOffset? ProviderDateSent { get; private set; }

    /// <summary>True for the "how did delivery go?" survey queued with the provider for later.</summary>
    public bool IsFollowUp { get; private set; }

    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message content has been disposed of at the provider and here.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>True when the provider never accepted the message (the send itself failed).</summary>
    public bool SendFailed { get; private set; }

    /// <summary>The caller-supplied idempotency key under which a resend produced this notification.</summary>
    public string? ResendIdempotencyKey { get; private set; }

    /// <summary>When this notification is a resend, the id of the notification it re-sent.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records a message the provider accepted: its SID, the sender, the current status.</summary>
    public void RecordAccepted(string providerSid, string? providerFrom, string? deliveryStatus, DateTimeOffset? providerDateSent)
    {
        ProviderSid = Guard.Against.NullOrEmpty(providerSid, nameof(providerSid));
        ProviderFrom = providerFrom;
        DeliveryStatus = deliveryStatus;
        ProviderDateSent = providerDateSent;
        SendFailed = false;
    }

    /// <summary>Records that the provider never accepted the message.</summary>
    public void RecordSendFailed(string? reason)
    {
        SendFailed = true;
        DeliveryStatus = "send_failed";
        ProviderErrorMessage = reason;
    }

    /// <summary>Refreshes the delivery outcome from a later read of the provider's record.</summary>
    public void UpdateDeliveryState(string? deliveryStatus, int? errorCode, string? errorMessage, DateTimeOffset? providerDateSent)
    {
        if (deliveryStatus != null) DeliveryStatus = deliveryStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (providerDateSent != null) ProviderDateSent = providerDateSent;
    }

    /// <summary>Marks the message as cancelled at the provider (a scheduled message that was called off).</summary>
    public void MarkCancelled() => DeliveryStatus = "canceled";

    /// <summary>Disposes of the message content here (the provider redaction is done separately).</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }

    public void MarkAsResendOf(int originalNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = originalNotificationId;
        ResendIdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
    }
}
