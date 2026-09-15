using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of one SMS the shop sent (or scheduled) about an order. It carries enough of the
/// state the provider owns — the provider's message identifier and the current delivery outcome —
/// that a later request can act on it (cancel, resend, redact, reconcile) and report on it, not
/// only the request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string ownerId, NotificationType type, string toPhoneNumber, string body)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));
        Guard.Against.Null(body, nameof(body));

        OrderId = orderId;
        OwnerId = ownerId;
        Type = type;
        ToPhoneNumber = toPhoneNumber;
        Body = body;
        Status = NotificationStatus.Queued;
    }

    public int OrderId { get; private set; }

    /// <summary>The shopper the order belongs to; scopes shopper-facing reads.</summary>
    public string OwnerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Destination E.164 number. Persisted so a resend can reach it again; never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>Local copy of the message text. Nulled when the content is disposed of.</summary>
    public string? Body { get; private set; }

    public bool ContentRedacted { get; private set; }

    /// <summary>The provider's own identifier for this message (Twilio message SID), once submitted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Last known delivery outcome; see <see cref="NotificationStatus"/>.</summary>
    public string Status { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>When a scheduled follow-up is due to be sent by the provider.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Caller-supplied idempotency key for the resend that produced this record (if any).</summary>
    public string? IdempotencyKey { get; private set; }

    public void SetIdempotencyKey(string key)
    {
        Guard.Against.NullOrEmpty(key, nameof(key));
        IdempotencyKey = key;
    }

    /// <summary>The message was accepted by the provider for immediate delivery.</summary>
    public void MarkSubmitted(string providerMessageSid, string status, int? errorCode, string? errorMessage, DateTimeOffset? sentAt)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = string.IsNullOrEmpty(status) ? NotificationStatus.Queued : status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        SentAt = sentAt ?? DateTimeOffset.UtcNow;
    }

    /// <summary>The message was accepted by the provider and scheduled to be sent later.</summary>
    public void MarkScheduled(string providerMessageSid, DateTimeOffset scheduledFor)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = NotificationStatus.Scheduled;
        ScheduledFor = scheduledFor;
    }

    /// <summary>The application could not hand the message to the provider at all.</summary>
    public void MarkSubmitFailed(string reason)
    {
        Status = NotificationStatus.SubmitFailed;
        ProviderErrorMessage = reason;
    }

    /// <summary>Refresh the delivery outcome from a later provider read.</summary>
    public void UpdateOutcome(string status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
            Status = status;
        if (errorCode.HasValue)
            ProviderErrorCode = errorCode;
        if (!string.IsNullOrEmpty(errorMessage))
            ProviderErrorMessage = errorMessage;
    }

    public void MarkCanceled()
    {
        Status = NotificationStatus.Canceled;
    }

    /// <summary>Dispose of the local copy of the content. The provider-side redaction is done separately.</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
