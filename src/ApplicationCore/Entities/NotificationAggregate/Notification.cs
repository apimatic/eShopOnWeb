using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS message the shop sent (or tried to send) to a shopper about one of their orders.
/// It carries the state the provider owns — the provider's message identifier and the current
/// delivery outcome — so a later request can act on the message (resend, cancel, redact) and
/// report on it without having been the request that originally sent it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }
    public int OrderId { get; private set; }
    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number. Sensitive: never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The provider's own identifier for this message (message SID). Null only if the provider never accepted it.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome for this message (e.g. queued, scheduled, sent, delivered, undelivered, failed, canceled).</summary>
    public string Status { get; private set; } = MessageStatuses.Pending;

    /// <summary>Local copy of the message body. Cleared when the content is disposed of.</summary>
    public string? Body { get; private set; }

    public bool ContentRedacted { get; private set; }

    /// <summary>When set, this message was queued with the provider to be sent at this future time.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    public bool IsScheduled { get; private set; }

    /// <summary>Caller-supplied idempotency key for a resend, so repeats under the same key do not send again.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>For a resend, the notification whose delivery this attempt is retrying.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    public Notification(string buyerId, int orderId, NotificationKind kind, string toNumber, string? body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        BuyerId = buyerId;
        OrderId = orderId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
    }

    /// <summary>Record the outcome of an immediate send: the provider's message id and its status.</summary>
    public void RecordSent(string? providerMessageSid, string status)
    {
        ProviderMessageSid = providerMessageSid;
        Status = status;
        IsScheduled = false;
        ScheduledSendAt = null;
    }

    /// <summary>Record that this message was queued with the provider for future delivery.</summary>
    public void RecordScheduled(string? providerMessageSid, string status, DateTimeOffset sendAt)
    {
        ProviderMessageSid = providerMessageSid;
        Status = status;
        IsScheduled = true;
        ScheduledSendAt = sendAt;
    }

    /// <summary>Record that the send could not be handed to the provider at all.</summary>
    public void RecordSendFailed()
    {
        ProviderMessageSid = null;
        Status = MessageStatuses.Failed;
        IsScheduled = false;
    }

    public void UpdateStatus(string status)
    {
        if (!string.IsNullOrEmpty(status))
        {
            Status = status;
        }
    }

    public void MarkCanceled()
    {
        Status = MessageStatuses.Canceled;
        IsScheduled = false;
    }

    /// <summary>Note that the provider-side content has been disposed of. The record and its outcome survive.</summary>
    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
    }

    public void MarkAsResendOf(int originalNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
    }
}
