using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS raised about an order — the record of what the shop tried to tell a shopper and what
/// became of it. It carries enough of the state the provider owns (its message SID and current delivery
/// outcome) that a later request can act on it and report on it, not merely the one that sent it.
///
/// The destination number is held so a message can be re-sent, but it is sensitive and must never be logged.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    private OrderNotification(int orderId, string buyerId, string toPhoneNumber, NotificationType type, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ToPhoneNumber = toPhoneNumber;
        Type = type;
        Body = body;
        Status = NotificationStatus.SubmitFailed; // until a send is recorded
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Create a fresh notification for an order event (placed / dispatched / cancelled).</summary>
    public static OrderNotification ForEvent(int orderId, string buyerId, string toPhoneNumber, NotificationType type, string body)
        => new(orderId, buyerId, toPhoneNumber, type, body);

    /// <summary>
    /// Create a notification produced by an operator re-send, carrying the caller-supplied idempotency key
    /// and a link back to the message it re-sends.
    /// </summary>
    public static OrderNotification ForResend(OrderNotification source, string idempotencyKey, string body)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        var resend = new OrderNotification(source.OrderId, source.BuyerId, source.ToPhoneNumber, source.Type, body)
        {
            IdempotencyKey = idempotencyKey,
            ResendOfNotificationId = source.Id
        };
        return resend;
    }

    public int OrderId { get; private set; }

    /// <summary>Owner (shopper identity) this notification is about — used to scope shopper access.</summary>
    public string BuyerId { get; private set; }

    /// <summary>Destination number in E.164. Sensitive: never logged, never returned in a listing.</summary>
    public string ToPhoneNumber { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>The provider's identifier for the message, once it accepted one. Null if the send never got that far.</summary>
    public string? ProviderMessageSid { get; private set; }

    public NotificationStatus Status { get; private set; }

    /// <summary>The provider's own status wire value, kept verbatim for fidelity in reports.</summary>
    public string? ProviderStatusRaw { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>The message text. Cleared once the shopper asks for the content to be disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>True once the content has been disposed of (redacted at the provider and cleared here).</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>For a scheduled follow-up, when the provider is due to send it.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>Caller-supplied idempotency key for a re-send, so repeating a request under the same key sends nothing new.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>If this notification is a re-send, the id of the notification it re-sends.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Record that the provider accepted the message and assigned it a SID and status.</summary>
    public void MarkSubmitted(string providerMessageSid, NotificationStatus status, string? providerStatusRaw,
        int? errorCode, string? errorMessage, DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ProviderStatusRaw = providerStatusRaw;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ScheduledSendAt = scheduledSendAt;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Record that the send attempt failed before the provider ever accepted a message.</summary>
    public void MarkSubmitFailed(string? errorMessage)
    {
        Status = NotificationStatus.SubmitFailed;
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Refresh the delivery outcome from the provider's latest word on the message.</summary>
    public void UpdateDeliveryState(NotificationStatus status, string? providerStatusRaw, int? errorCode, string? errorMessage)
    {
        Status = status;
        ProviderStatusRaw = providerStatusRaw;
        if (errorCode.HasValue) ErrorCode = errorCode;
        if (!string.IsNullOrEmpty(errorMessage)) ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Mark a scheduled follow-up as called off before it went out.</summary>
    public void MarkCanceled()
    {
        Status = NotificationStatus.Canceled;
        ProviderStatusRaw = "canceled";
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Dispose of the message content locally. The provider-side redaction is done by the caller.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
