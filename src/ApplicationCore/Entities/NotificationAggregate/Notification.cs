using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS message about an order. Carries enough of the state the provider owns — its message
/// identifier (<see cref="ProviderMessageSid"/>) and current delivery outcome (<see cref="Status"/>) —
/// that a later request can act on it (cancel, re-send, redact) and report on it.
/// <see cref="ToNumber"/> and <see cref="Body"/> are never written to logs.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>The shopper the message is about — used to scope a shopper's view to their own data.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination E.164 number. Never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>Message text. Redactable — cleared when the shopper asks for the content to be disposed of.</summary>
    public string Body { get; private set; }

    /// <summary>The provider's message identifier (SID). Null until the provider has accepted the message.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Current delivery outcome — a provider wire status or a local sentinel (see <see cref="NotificationStatus"/>).</summary>
    public string Status { get; private set; } = NotificationStatus.Pending;

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>True when this message was queued with the provider to send at a future time.</summary>
    public bool IsScheduled { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>Caller-supplied idempotency key, set only on a message produced by an operator re-send.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this message was produced by re-sending an earlier one, the earlier one's id.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>True once the message content has been disposed of (locally cleared and redacted at the provider).</summary>
    public bool ContentRedacted { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    public Notification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string toNumber,
        string body,
        bool isScheduled = false,
        DateTimeOffset? scheduledSendAt = null,
        string? idempotencyKey = null,
        int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body ?? string.Empty;
        IsScheduled = isScheduled;
        ScheduledSendAt = scheduledSendAt;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
        Status = NotificationStatus.Pending;
    }

    /// <summary>Records that the provider accepted the message, capturing its SID and current status.</summary>
    public void MarkSent(string sid, string status, int? errorCode = null, string? errorMessage = null)
    {
        ProviderMessageSid = sid;
        Status = string.IsNullOrEmpty(status) ? NotificationStatus.Queued : status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Records that the provider accepted a future-dated message.</summary>
    public void MarkScheduled(string sid, string status)
    {
        IsScheduled = true;
        ProviderMessageSid = sid;
        Status = string.IsNullOrEmpty(status) ? NotificationStatus.Scheduled : status;
        Touch();
    }

    /// <summary>Records that the message could not be handed to the provider at all (no SID).</summary>
    public void MarkSendFailed(string? errorMessage, int? errorCode = null)
    {
        Status = NotificationStatus.SendFailed;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
        Touch();
    }

    /// <summary>Refreshes the delivery outcome from a later read of the provider's record.</summary>
    public void UpdateDeliveryStatus(string status, int? errorCode = null, string? errorMessage = null)
    {
        if (string.IsNullOrEmpty(status))
        {
            return;
        }

        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Records that a scheduled message was cancelled before it went out.</summary>
    public void MarkCancelled(string? status = null)
    {
        Status = string.IsNullOrEmpty(status) ? NotificationStatus.Canceled : status;
        Touch();
    }

    /// <summary>
    /// Disposes of the message content locally. The record of the message (SID, status, outcome) survives.
    /// </summary>
    public void RedactContent()
    {
        Body = string.Empty;
        ContentRedacted = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
