using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

/// <summary>
/// A single SMS the shop sent (or tried to send, or scheduled) to a shopper about one of their
/// orders. It carries enough of the state the provider owns — the provider's message identifier
/// and current delivery outcome — that a later request can act on it (re-send, dispose of its
/// content, cancel a scheduled follow-up) and report on it, not only the request that sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    private OrderNotification(int orderId, string ownerId, NotificationKind kind, string toNumber, string? body)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        OwnerId = ownerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Factory for a fresh notification about an order, before it has been handed to the provider.</summary>
    public static OrderNotification ForOrder(int orderId, string ownerId, NotificationKind kind, string toNumber, string body)
        => new(orderId, ownerId, kind, toNumber, body);

    public int OrderId { get; private set; }

    /// <summary>The shopper the message is about (owner of the order and the destination number).</summary>
    public string OwnerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The destination number in E.164. Treated as PII — never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Cleared once its content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for this message (Twilio SID), once it has been accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome for this message. See <see cref="MessageDeliveryStatus"/>.</summary>
    public string? Status { get; private set; }

    /// <summary>Provider error code when the message failed or was undelivered.</summary>
    public int? ErrorCode { get; private set; }

    /// <summary>Provider error description when the message failed or was undelivered.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>True for the delayed follow-up that the provider holds and sends later.</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>When a scheduled message is due to be sent by the provider.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>True once the message content has been disposed of (redacted at the provider and cleared here).</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>If this notification was produced by a re-send, the notification it was re-sending.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>The caller-supplied idempotency key under which a re-send produced this notification.</summary>
    public string? ResendIdempotencyKey { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }
    public DateTimeOffset? UpdatedDate { get; private set; }

    /// <summary>Records that the provider accepted the message and assigned it an identifier and status.</summary>
    public void RecordAccepted(string providerMessageSid, string status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the provider could not be reached, so the message never went out.</summary>
    public void RecordNotSent(string reason)
    {
        Status = MessageDeliveryStatus.NotSent;
        ErrorMessage = reason;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Refreshes the delivery outcome from the provider's current view of the message.</summary>
    public void UpdateDeliveryState(string status, int? errorCode, string? errorMessage)
    {
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks this notification as a scheduled (future) send.</summary>
    public void MarkScheduled(DateTimeOffset scheduledFor)
    {
        IsScheduled = true;
        ScheduledFor = scheduledFor;
    }

    /// <summary>Records that this notification is a re-send of an earlier one, under an idempotency key.</summary>
    public void MarkResendOf(int sourceNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = sourceNotificationId;
        ResendIdempotencyKey = idempotencyKey;
    }

    /// <summary>Clears the stored message text after its content has been disposed of at the provider.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
        UpdatedDate = DateTimeOffset.UtcNow;
    }
}
