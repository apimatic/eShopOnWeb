using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop tried (or is scheduled) to send to a shopper about one of their orders,
/// together with enough of the provider's own state — its message identifier and current delivery
/// outcome — that a later request can act on it (resend, cancel, dispose) and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    private OrderNotification(int orderId, string buyerId, NotificationType type, string? toPhoneNumber, string? body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToPhoneNumber = toPhoneNumber;
        Body = body;
        Status = NotificationStatus.NotSent;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>Owner of the order this notification is about (the JWT name claim).</summary>
    public string BuyerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Destination in canonical E.164. PII — never log this.</summary>
    public string? ToPhoneNumber { get; private set; }

    /// <summary>
    /// The message text. Stored so a resend can reproduce it; cleared when the shopper asks for the
    /// content to be disposed of.
    /// </summary>
    public string? Body { get; private set; }

    /// <summary>The provider's message identifier (Twilio SID), once a send has been accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Current delivery outcome. See <see cref="NotificationStatus"/>.</summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When a scheduled message is due to be sent by the provider (follow-ups only).</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True for the "how did delivery go" message queued for a few days after dispatch.</summary>
    public bool IsFollowUp { get; private set; }

    /// <summary>True once the message body has been disposed of, at the provider and locally.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>Caller-supplied key that produced this notification via a resend, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The notification this one was created to re-send, if any.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public static OrderNotification Create(int orderId, string buyerId, NotificationType type, string toPhoneNumber, string body)
        => new(orderId, buyerId, type, toPhoneNumber, body);

    /// <summary>A notification for a shopper who has no number on file — recorded but never sent.</summary>
    public static OrderNotification CreateNotSent(int orderId, string buyerId, NotificationType type, string body)
        => new(orderId, buyerId, type, toPhoneNumber: null, body);

    public void MarkAsFollowUp(DateTimeOffset scheduledSendAt)
    {
        IsFollowUp = true;
        ScheduledSendAt = scheduledSendAt;
    }

    public void MarkAsResendOf(int originalNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>Record the provider's acceptance of a send (its SID and initial status).</summary>
    public void RecordAccepted(string providerMessageSid, string status, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        Status = string.IsNullOrEmpty(status) ? NotificationStatus.Queued : status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Record that the send could not even be handed to the provider.</summary>
    public void RecordSendFailed(int? errorCode, string? errorMessage)
    {
        Status = NotificationStatus.NotSent;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Refresh the delivery outcome from a later read of the provider's record.</summary>
    public void UpdateDeliveryState(string status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
        {
            Status = status;
        }

        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void MarkCanceled() => Status = NotificationStatus.Canceled;

    /// <summary>
    /// Dispose of the message content locally. The caller is responsible for also redacting it at
    /// the provider. The record of the send and its outcome is deliberately preserved.
    /// </summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }
}
