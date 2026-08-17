using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS eShop tried to send a shopper as one of their orders moved. It carries enough of
/// the state the provider owns — the provider's identifier (<see cref="ProviderMessageSid"/>) and
/// its current delivery outcome (<see cref="ProviderStatus"/>) — that a later request (resend,
/// cancel a scheduled follow-up, redact content, reconcile) can act on it and report on it, not
/// only the request that first sent it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }

    public Notification(string ownerId, int orderId, NotificationKind kind, string toPhoneNumber, string body)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OwnerId = ownerId;
        OrderId = orderId;
        Kind = kind;
        ToPhoneNumber = toPhoneNumber;
        Body = body;
        State = NotificationState.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>Identity (user name) of the shopper this message is about.</summary>
    public string OwnerId { get; private set; }

    public int OrderId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Canonical E.164 destination. Sensitive: never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The message text. Null once the content has been redacted. Sensitive: never logged.</summary>
    public string? Body { get; private set; }

    public bool ContentRedacted { get; private set; }

    public NotificationState State { get; private set; }

    /// <summary>The provider's own identifier for this message (Twilio message SID).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (e.g. queued, sent, delivered, undelivered, failed, scheduled, canceled).</summary>
    public string? ProviderStatus { get; private set; }

    /// <summary>The provider's error code when a send failed or was undelivered; otherwise null.</summary>
    public int? ProviderErrorCode { get; private set; }

    /// <summary>When set, the provider was asked to send this message at that time (the delivery-feedback follow-up).</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>When eShop handed the message to the provider. Used to line messages up during reconciliation.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Caller-supplied idempotency key of the resend that produced this message (null for lifecycle notifications).</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this message is a resend, the id of the notification it re-sent.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void MarkSent(string providerMessageSid, string? providerStatus, int? providerErrorCode)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
        State = NotificationState.Sent;
        SentAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void MarkScheduled(string providerMessageSid, string? providerStatus, DateTimeOffset sendAt)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ScheduledSendAt = sendAt;
        State = NotificationState.Scheduled;
        Touch();
    }

    public void MarkFailed(string? providerStatus, int? providerErrorCode)
    {
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
        State = NotificationState.Failed;
        Touch();
    }

    public void MarkCancelled(string? providerStatus)
    {
        if (providerStatus != null)
        {
            ProviderStatus = providerStatus;
        }
        State = NotificationState.Cancelled;
        Touch();
    }

    /// <summary>Records the provider's latest delivery outcome, fetched on demand.</summary>
    public void UpdateProviderStatus(string? providerStatus, int? providerErrorCode)
    {
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
        Touch();
    }

    /// <summary>
    /// Drops the message text locally after it has been disposed of on the provider side.
    /// The fact that a message was sent, and what became of it, deliberately survives.
    /// </summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        Touch();
    }

    public void MarkAsResendOf(int originalNotificationId, string? idempotencyKey)
    {
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
