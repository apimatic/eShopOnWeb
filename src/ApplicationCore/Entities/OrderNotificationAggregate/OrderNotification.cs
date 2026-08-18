using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// The record of a single SMS the shop sent (or tried to send) to a shopper about one order.
/// It carries enough of the state the provider owns &ndash; the message SID and the last-known
/// delivery outcome &ndash; that a later request can act on it (resend, cancel, dispose content)
/// and report on it, without the message that created it still being in flight.
/// <see cref="ToNumber"/> is the shopper's number and is PII: it must never be written to logs
/// and is never returned by an endpoint.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order this message is about (their identity/email).</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The destination number in E.164. PII &ndash; never log or return this.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The provider's identifier for the message (its message SID). Null only if the provider never accepted it.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The last-known status observed from the provider (see <see cref="NotificationStatus"/>).</summary>
    public string ProviderStatus { get; private set; } = NotificationStatus.SendError;

    public int? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>True once the message body has been redacted at the provider on the shopper's request.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>When a scheduled message (the delivery follow-up) is queued with the provider to go out.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>The caller-supplied idempotency key, set only on a notification produced by a resend.</summary>
    public string? ResendIdempotencyKey { get; private set; }

    /// <summary>The id of the notification this one was a re-send of, if any.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, NotificationKind kind, string toNumber)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Kind = kind;
        ToNumber = Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
    }

    /// <summary>Record that the provider accepted the message and returned an identifier and initial state.</summary>
    public void RecordAccepted(string providerMessageSid, string status, int? errorCode, string? errorMessage, DateTimeOffset? scheduledSendAt)
    {
        ProviderMessageSid = Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderStatus = Guard.Against.NullOrEmpty(status, nameof(status));
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ScheduledSendAt = scheduledSendAt;
    }

    /// <summary>Record that the provider never accepted the request, so no provider record exists.</summary>
    public void RecordSendError(int? errorCode, string? errorMessage)
    {
        ProviderStatus = NotificationStatus.SendError;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Advance the last-known delivery outcome from a fresh read of the provider's record.</summary>
    public void UpdateStatus(string status, int? errorCode, string? errorMessage)
    {
        if (string.IsNullOrEmpty(status)) return;
        ProviderStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void MarkContentRedacted() => ContentRedacted = true;

    public void SetResendMetadata(string idempotencyKey, int resendOfNotificationId)
    {
        ResendIdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        ResendOfNotificationId = resendOfNotificationId;
    }

    /// <summary>Whether this message is a scheduled one still awaiting its send time (and so still cancellable).</summary>
    public bool IsPendingScheduled =>
        ScheduledSendAt is not null &&
        ProviderMessageSid is not null &&
        ProviderStatus is NotificationStatus.Scheduled or NotificationStatus.Accepted;
}
