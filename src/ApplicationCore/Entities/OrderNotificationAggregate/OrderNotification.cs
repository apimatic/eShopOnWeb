using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or tried to send / scheduled) about an order, plus the state the
/// messaging provider owns for it — its identifier (<see cref="ProviderMessageSid"/>) and current
/// delivery outcome (<see cref="Status"/>) — so a later request can act on and report each message.
/// The destination number is stored for operational purposes but is never written to logs.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    private OrderNotification(int orderId, string buyerId, NotificationType type, string toPhoneNumber, string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToPhoneNumber = toPhoneNumber;
        Body = body;
        Status = NotificationDeliveryStatus.Pending;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Create a notification to be sent (or scheduled) immediately after persistence.</summary>
    public static OrderNotification Create(int orderId, string buyerId, NotificationType type, string toPhoneNumber, string body)
        => new(orderId, buyerId, type, toPhoneNumber, body);

    public int OrderId { get; private set; }

    /// <summary>The shopper that owns the order this message is about (used to scope shopper-facing reads).</summary>
    public string BuyerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Destination number (E.164). Stored for operations; must never be logged.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of at the shopper's request.</summary>
    public string? Body { get; private set; }

    public bool ContentDisposed { get; private set; }

    /// <summary>The provider's identifier for this message; null if the provider never accepted it.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Last-known status: a provider delivery status, or one of the app-level constants.</summary>
    public string Status { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>True for a message queued with the provider to go out later (the delivery follow-up).</summary>
    public bool IsScheduled { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>Caller-supplied idempotency key for a resend that produced this notification, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>The provider accepted the message for immediate delivery.</summary>
    public void MarkSent(string providerMessageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = string.IsNullOrEmpty(providerStatus) ? NotificationDeliveryStatus.Pending : providerStatus;
        IsScheduled = false;
        ProviderErrorCode = null;
        ProviderErrorMessage = null;
    }

    /// <summary>The provider accepted the message and is holding it to send at <paramref name="sendAt"/>.</summary>
    public void MarkScheduled(string providerMessageSid, string providerStatus, DateTimeOffset sendAt)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = string.IsNullOrEmpty(providerStatus) ? NotificationDeliveryStatus.Scheduled : providerStatus;
        IsScheduled = true;
        ScheduledSendAt = sendAt;
    }

    /// <summary>The provider never accepted the message (rejected the request or the destination is unreachable).</summary>
    public void MarkSendFailed(int? errorCode, string? errorMessage)
    {
        Status = NotificationDeliveryStatus.FailedToSend;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Refresh the stored delivery outcome from the provider.</summary>
    public void UpdateDeliveryStatus(string providerStatus, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(providerStatus))
            Status = providerStatus;
        if (errorCode.HasValue) ProviderErrorCode = errorCode;
        if (!string.IsNullOrEmpty(errorMessage)) ProviderErrorMessage = errorMessage;
    }

    /// <summary>A pending scheduled message was called off at the provider before it went out.</summary>
    public void MarkCanceled()
    {
        Status = NotificationDeliveryStatus.Canceled;
        IsScheduled = false;
    }

    /// <summary>
    /// The message content has been disposed of (redacted at the provider and cleared here). The record of
    /// the message having been sent, and what became of it, survives.
    /// </summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }

    public void SetIdempotencyKey(string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        IdempotencyKey = idempotencyKey;
    }
}
