using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of one text message the shop sent (or scheduled) to a shopper about one of their
/// orders. It carries enough of the state the provider owns — the message identifier and its
/// current delivery outcome — that a later request can act on it (re-send, cancel, redact) and
/// report on it, not only the request that first created it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }

    private Notification(int orderId, string buyerId, NotificationKind kind, string toPhoneNumber, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToPhoneNumber = toPhoneNumber;
        Body = body;
        DeliveryStatus = NotificationDeliveryStatus.SendFailed; // until a provider result is recorded
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Creates a notification for an immediate ("send now") message.</summary>
    public static Notification ForImmediate(int orderId, string buyerId, NotificationKind kind, string toPhoneNumber, string body)
        => new(orderId, buyerId, kind, toPhoneNumber, body);

    /// <summary>Creates a notification for a message scheduled to go out at a future time.</summary>
    public static Notification ForScheduled(int orderId, string buyerId, NotificationKind kind, string toPhoneNumber, string body, DateTimeOffset sendAt)
    {
        var n = new Notification(orderId, buyerId, kind, toPhoneNumber, body)
        {
            IsScheduled = true,
            ScheduledFor = sendAt
        };
        return n;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>Identity (username) of the shopper the message is for. Used to scope shopper access.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number in E.164. Never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The message text. Nulled out once the content is disposed of on request.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for this message, once it has one.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Last-known delivery outcome, as owned by the provider (or a local status).</summary>
    public string DeliveryStatus { get; private set; }

    /// <summary>Provider error code for a failed/undelivered message, if any.</summary>
    public string? ErrorCode { get; private set; }

    public bool IsScheduled { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Caller-supplied idempotency key for the resend that produced this notification, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>If this notification was produced by a re-send, the notification it re-sent.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>True once the message content has been disposed of at the shopper's request.</summary>
    public bool ContentRedacted { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Records the provider's result for a successfully created message.</summary>
    public void RecordProviderResult(string providerMessageSid, string status, string? errorCode)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = string.IsNullOrEmpty(status) ? DeliveryStatus : status;
        ErrorCode = errorCode;
    }

    /// <summary>Records that the message could not be created at the provider at all.</summary>
    public void RecordSendFailure()
    {
        DeliveryStatus = NotificationDeliveryStatus.SendFailed;
    }

    /// <summary>Refreshes the delivery outcome from a later provider read.</summary>
    public void UpdateDeliveryStatus(string status, string? errorCode)
    {
        if (string.IsNullOrEmpty(status)) return;
        DeliveryStatus = status;
        ErrorCode = errorCode;
    }

    /// <summary>Marks a scheduled message as called off.</summary>
    public void MarkCanceled()
    {
        DeliveryStatus = NotificationDeliveryStatus.Canceled;
    }

    /// <summary>Disposes of the message text locally. The provider copy is redacted separately.</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public void SetResendMetadata(int resendOfNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
    }
}
