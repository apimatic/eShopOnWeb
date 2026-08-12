using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or scheduled) about an order, together with the provider state the
/// integration needs to keep to act on the message later and report on it: the provider's own
/// message identifier and the last known delivery outcome. The destination number and the message
/// text are personal data — never logged.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    /// <summary>Identity (user name) of the shopper the order — and therefore this message — belongs to.</summary>
    public string OwnerId { get; private set; }

    public int OrderId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The canonical E.164 destination the message went to. Personal data — never logged, masked when exposed.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of at the shopper's request.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own identifier for this message (its SID). Null only if the provider never accepted it.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Last known provider delivery status (a <see cref="ProviderMessageStatus"/> wire value).</summary>
    public string? ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>Set when the message could not be handed to the provider at all (so there is no SID/status).</summary>
    public string? SendFailureReason { get; private set; }

    /// <summary>True while this is a future-dated message the provider has queued and not yet sent.</summary>
    public bool IsScheduled { get; private set; }

    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once a scheduled message was called off before it went out.</summary>
    public bool ScheduleCancelled { get; private set; }

    /// <summary>True once the message text has been redacted at the provider and cleared locally.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this message via a resend, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The notification this one was created to re-send, if it originated from a resend.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(string ownerId, int orderId, NotificationKind kind, string toNumber, string body,
        bool isScheduled = false, DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OwnerId = ownerId;
        OrderId = orderId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        IsScheduled = isScheduled;
        ScheduledSendAt = scheduledSendAt;
    }

    /// <summary>Records that the provider accepted the message, capturing its SID and initial status.</summary>
    public void RecordSent(string providerMessageSid, string? status)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = status;
        SendFailureReason = null;
        ProviderErrorCode = null;
        ProviderErrorMessage = null;
    }

    /// <summary>
    /// Records that the message could not be handed to the provider (no SID). Nothing is queued, so a
    /// message that was meant to be scheduled is no longer pending. The order operation still succeeds.
    /// </summary>
    public void RecordSendFailure(string reason)
    {
        SendFailureReason = string.IsNullOrWhiteSpace(reason) ? "send failed" : reason;
        IsScheduled = false;
    }

    /// <summary>Refreshes the last-known delivery outcome from the provider.</summary>
    public void UpdateDeliveryStatus(string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
        {
            ProviderStatus = status;
        }
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (!IsScheduled) return;
        // A scheduled message that has left the "scheduled" state is no longer pending cancellation.
        if (!string.Equals(status, ProviderMessageStatus.Scheduled, StringComparison.OrdinalIgnoreCase))
        {
            IsScheduled = false;
        }
    }

    /// <summary>Marks a scheduled message as called off before it went out.</summary>
    public void MarkScheduleCancelled()
    {
        ScheduleCancelled = true;
        IsScheduled = false;
        ProviderStatus = ProviderMessageStatus.Canceled;
    }

    /// <summary>Disposes of the message text after it has been redacted at the provider.</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }

    internal void SetResendOrigin(int originalNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>
    /// An operator may re-send a message that did not reach the shopper: one that the carrier refused
    /// (undelivered/failed) or that never made it to the provider. Scheduled messages are excluded.
    /// </summary>
    public bool DidNotReachRecipient =>
        !IsScheduled &&
        (SendFailureReason != null || ProviderMessageStatus.IsUndeliverable(ProviderStatus));
}
