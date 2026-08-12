using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single message the shop sent (or tried to send) to a shopper about one of their orders.
/// It carries enough of the provider's own state — the message identifier and the current
/// delivery outcome — that a later request can act on it (cancel, resend, dispose, reconcile)
/// and report on it, not only the request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string ownerId, NotificationKind kind, string toPhoneNumber, string? messageBody)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));

        OrderId = orderId;
        OwnerId = ownerId;
        Kind = kind;
        ToPhoneNumber = toPhoneNumber;
        MessageBody = messageBody;
        DeliveryStatus = DeliveryStatuses.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>The shopper the message is about/for (their identity/user name).</summary>
    public string OwnerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Canonical E.164 destination. Never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio Message SID). Null if the provider never accepted it.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (verbatim), or an app-only value if it was never accepted.</summary>
    public string DeliveryStatus { get; private set; }

    /// <summary>The provider's error code for a failed message, if any.</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>The message text. Cleared once the content is disposed of.</summary>
    public string? MessageBody { get; private set; }

    /// <summary>True once the shopper asked for the content to be disposed of.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>True for a message queued with the provider for future delivery (the delivery follow-up).</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>When a scheduled message is due to go out.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>When the provider reports the message was actually sent.</summary>
    public DateTimeOffset? ProviderSentAt { get; private set; }

    /// <summary>Caller-supplied idempotency key for a resend; ensures a repeated request does not send again.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>For a resend, the notification whose message this re-sends.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkScheduled(DateTimeOffset scheduledForUtc)
    {
        IsScheduled = true;
        ScheduledFor = scheduledForUtc;
    }

    public void SetIdempotencyKey(string idempotencyKey) => IdempotencyKey = idempotencyKey;

    public void SetResendOf(int originalNotificationId) => ResendOfNotificationId = originalNotificationId;

    /// <summary>Records the provider's response to submitting the message.</summary>
    public void ApplyProviderResult(string? sid, string status, string? errorCode, DateTimeOffset? sentAt)
    {
        if (!string.IsNullOrEmpty(sid))
        {
            ProviderMessageSid = sid;
        }
        if (!string.IsNullOrEmpty(status))
        {
            DeliveryStatus = status;
        }
        ErrorCode = errorCode;
        if (sentAt.HasValue)
        {
            ProviderSentAt = sentAt;
        }
    }

    /// <summary>Updates the delivery outcome from a later provider read.</summary>
    public void UpdateDeliveryState(string status, string? errorCode, DateTimeOffset? sentAt)
    {
        if (!string.IsNullOrEmpty(status))
        {
            DeliveryStatus = status;
        }
        if (!string.IsNullOrEmpty(errorCode))
        {
            ErrorCode = errorCode;
        }
        if (sentAt.HasValue)
        {
            ProviderSentAt = sentAt;
        }
    }

    /// <summary>The provider never accepted the message (network/error before a SID existed).</summary>
    public void MarkSendFailed(string? reason)
    {
        DeliveryStatus = DeliveryStatuses.SendFailed;
        if (!string.IsNullOrEmpty(reason))
        {
            ErrorCode = reason;
        }
    }

    /// <summary>The scheduled message was called off before it went out.</summary>
    public void MarkCanceled()
    {
        DeliveryStatus = DeliveryStatuses.Canceled;
    }

    /// <summary>Disposes of the message content locally; provider-side redaction is handled by the caller.</summary>
    public void DisposeContent()
    {
        MessageBody = null;
        ContentRedacted = true;
    }

    /// <summary>Whether this scheduled message can still be called off with the provider.</summary>
    public bool CanBeCancelled()
        => IsScheduled
           && !string.IsNullOrEmpty(ProviderMessageSid)
           && DeliveryStatus.Equals(DeliveryStatuses.Scheduled, StringComparison.OrdinalIgnoreCase);
}
