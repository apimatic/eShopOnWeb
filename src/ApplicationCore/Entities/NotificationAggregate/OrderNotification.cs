using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS notification raised about an order. It carries enough of the state the provider
/// owns — the provider's message identifier and the current delivery outcome — that a later
/// request can act on it (resend, cancel, dispose content) and report on it (my-orders,
/// per-order notifications, reconciliation), not only the request that created it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { } // EF

    public OrderNotification(int orderId, string buyerId, NotificationType type, string body, string? toNumber)
    {
        OrderId = orderId;
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Type = type;
        Body = Guard.Against.Null(body, nameof(body));
        ToNumber = toNumber;
        Status = NotificationDeliveryStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>The order this notification is about (references the app's existing Order aggregate).</summary>
    public int OrderId { get; private set; }

    /// <summary>The owning shopper (the order's buyer). Used to scope shopper-facing reads.</summary>
    public string BuyerId { get; private set; } = default!;

    public NotificationType Type { get; private set; }

    /// <summary>The destination number (canonical E.164). Null when there was no number on file. PII — never logged.</summary>
    public string? ToNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of at the shopper's request.</summary>
    public string? Body { get; private set; }

    public bool ContentDisposed { get; private set; }

    /// <summary>The provider's own identifier for the message (its SID). Null if no message was created.</summary>
    public string? ProviderMessageId { get; private set; }

    public NotificationDeliveryStatus Status { get; private set; }

    /// <summary>The raw status string as reported by the provider, kept verbatim for fidelity.</summary>
    public string? ProviderStatusRaw { get; private set; }

    public int? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>True for the delivery-feedback follow-up that is queued with the provider for later.</summary>
    public bool IsFollowUp { get; private set; }

    /// <summary>When a scheduled (follow-up) message is due to be sent by the provider.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>The provider's timestamp of when the message was actually sent.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Set on a notification produced by re-sending an earlier one; points at that earlier notification.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkAsFollowUp(DateTimeOffset scheduledFor)
    {
        IsFollowUp = true;
        ScheduledFor = scheduledFor;
        Touch();
    }

    public void MarkAsResendOf(int originNotificationId)
    {
        ResendOfNotificationId = originNotificationId;
        Touch();
    }

    /// <summary>No message was created because the shopper has no number on file.</summary>
    public void MarkNotSent()
    {
        Status = NotificationDeliveryStatus.NotSent;
        ProviderMessageId = null;
        Touch();
    }

    /// <summary>The provider accepted the message; record its identifier and reported status.</summary>
    public void RecordAccepted(string providerMessageId, NotificationDeliveryStatus status, string? providerStatusRaw,
        int? errorCode, string? errorMessage, DateTimeOffset? sentAt)
    {
        ProviderMessageId = Guard.Against.NullOrEmpty(providerMessageId, nameof(providerMessageId));
        Status = status;
        ProviderStatusRaw = providerStatusRaw;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        SentAt = sentAt;
        Touch();
    }

    /// <summary>The provider call to create the message failed; the notification is a record of the failed attempt.</summary>
    public void RecordSendError(string? errorMessage)
    {
        Status = NotificationDeliveryStatus.SendError;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Refresh the delivery outcome from the provider's current view of the message.</summary>
    public void ApplyProviderState(NotificationDeliveryStatus status, string? providerStatusRaw, int? errorCode,
        string? errorMessage, DateTimeOffset? sentAt)
    {
        Status = status;
        ProviderStatusRaw = providerStatusRaw;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        if (sentAt.HasValue)
        {
            SentAt = sentAt;
        }
        Touch();
    }

    /// <summary>The content has been disposed of. The record survives; only the text is gone.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
