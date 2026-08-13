using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single message that eShop raised about an order as it moved through its lifecycle. It records
/// both what eShop intended (order, kind, recipient, body) and the state the provider owns for the
/// message once it has been handed over: the provider's identifier and current delivery outcome, so
/// a later request can act on and report on it rather than only the one that sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    private OrderNotification(int orderId, string ownerId, NotificationKind kind, string? toPhoneNumber, string? body)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        OrderId = orderId;
        OwnerId = ownerId;
        Kind = kind;
        ToPhoneNumber = toPhoneNumber;
        Body = body;
        State = NotificationDeliveryState.Pending;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Creates a notification that is about to be sent to the given destination.
    /// </summary>
    public static OrderNotification ForSending(int orderId, string ownerId, NotificationKind kind, string toPhoneNumber, string body)
    {
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));
        return new OrderNotification(orderId, ownerId, kind, toPhoneNumber, body);
    }

    /// <summary>
    /// Creates a notification recording that nothing was sent because the shopper had no number on file.
    /// Carries no phone number.
    /// </summary>
    public static OrderNotification NotAttempted(int orderId, string ownerId, NotificationKind kind)
    {
        var notification = new OrderNotification(orderId, ownerId, kind, null, null)
        {
            State = NotificationDeliveryState.NotAttempted
        };
        return notification;
    }

    public int OrderId { get; private set; }

    /// <summary>Identity of the shopper the order belongs to; used to scope who may see the notification.</summary>
    public string OwnerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination in E.164 form. Never written to logs.</summary>
    public string? ToPhoneNumber { get; private set; }

    /// <summary>The text of the message. Cleared (null) once its content has been disposed of.</summary>
    public string? Body { get; private set; }

    // ----- Provider-owned state -----

    /// <summary>The provider's identifier for the message (its SID), once accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome for the message (its raw status string).</summary>
    public string? ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    // ----- eShop-side lifecycle -----

    public NotificationDeliveryState State { get; private set; }

    /// <summary>True once the message content has been disposed of at the provider and here.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>When set, the message was scheduled with the provider to go out at this time.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this notification (resend only).</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this notification is a re-send, the id of the notification it re-sent.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    public DateTimeOffset? UpdatedDate { get; private set; }

    /// <summary>Records that the provider accepted the message and owns it from here.</summary>
    public void MarkSent(string providerMessageSid, string? providerStatus, int? errorCode, string? errorMessage, DateTimeOffset? scheduledFor)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ScheduledFor = scheduledFor;
        State = NotificationDeliveryState.Sent;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the message could not be handed to the provider. The reason must not contain the phone number.</summary>
    public void MarkSendFailed(string reason)
    {
        ProviderErrorMessage = reason;
        State = NotificationDeliveryState.FailedToSend;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Refreshes the provider-owned delivery outcome (from a fetch of the message).</summary>
    public void UpdateDeliveryStatus(string? providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (State == NotificationDeliveryState.Cancelled && !string.Equals(providerStatus, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            // Keep an explicit cancellation sticky unless the provider still reports it as canceled.
        }
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that a scheduled message was cancelled with the provider before it went out.</summary>
    public void MarkCancelled()
    {
        State = NotificationDeliveryState.Cancelled;
        ProviderStatus = "canceled";
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks the local body as disposed of. The content is also redacted at the provider by the caller.</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        UpdatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>Links this notification to the one it re-sent and records the idempotency key that produced it.</summary>
    public void MarkAsResendOf(int originalNotificationId, string? idempotencyKey)
    {
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
    }
}
