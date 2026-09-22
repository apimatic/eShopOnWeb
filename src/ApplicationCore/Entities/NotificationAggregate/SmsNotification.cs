using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of one SMS the shop sent (or tried to send) to a shopper about an order. It carries
/// enough of the state the provider owns — the provider's identifier (<see cref="ProviderSid"/>)
/// and its current delivery outcome (<see cref="ProviderStatus"/>) — that a later request can act
/// on the message (resend, cancel, redact) and report on it, not only the request that sent it.
/// The recipient's phone number is stored here but is never written to logs.
/// </summary>
public class SmsNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SmsNotification() { }

    public SmsNotification(string ownerId, int orderId, string recipient, NotificationKind kind, string body)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(recipient, nameof(recipient));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OwnerId = ownerId;
        OrderId = orderId;
        Recipient = recipient;
        Kind = kind;
        Body = body;
        State = NotificationState.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The shopper the message is about (the order's buyer).</summary>
    public string OwnerId { get; private set; }

    /// <summary>The order this notification concerns.</summary>
    public int OrderId { get; private set; }

    /// <summary>The E.164 destination number.</summary>
    public string Recipient { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The message text; null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>True once the message content has been redacted at the provider and cleared here.</summary>
    public bool ContentRedacted { get; private set; }

    public NotificationState State { get; private set; }

    /// <summary>The provider's identifier for this message (its message SID).</summary>
    public string? ProviderSid { get; private set; }

    /// <summary>The provider's own current delivery status (wire value), refreshed from the provider.</summary>
    public string? ProviderStatus { get; private set; }

    public int? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>The provider's <c>date_sent</c> — the clock both sides reconcile on.</summary>
    public DateTimeOffset? ProviderDateSent { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The caller-supplied idempotency key, when this notification was produced by a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When produced by a resend, the notification it re-sent.</summary>
    public int? ResentFromNotificationId { get; private set; }

    public void MarkResendOf(int originalNotificationId, string idempotencyKey)
    {
        ResentFromNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>The provider accepted an immediate send.</summary>
    public void MarkSent(string? providerSid, string? providerStatus, DateTimeOffset? providerDateSent)
    {
        State = NotificationState.Sent;
        ProviderSid = providerSid;
        ProviderStatus = providerStatus;
        ProviderDateSent = providerDateSent;
        ErrorCode = null;
        ErrorMessage = null;
    }

    /// <summary>The provider accepted a scheduled send (a future delivery follow-up).</summary>
    public void MarkScheduled(string? providerSid, string? providerStatus)
    {
        State = NotificationState.Scheduled;
        ProviderSid = providerSid;
        ProviderStatus = providerStatus;
    }

    /// <summary>The provider explicitly rejected the request — a known, definite failure.</summary>
    public void MarkFailed(int? errorCode, string? errorMessage, string? providerStatus = null)
    {
        State = NotificationState.Failed;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        if (providerStatus is not null)
        {
            ProviderStatus = providerStatus;
        }
    }

    /// <summary>
    /// The transport failed after the request may have reached the provider. The outcome is
    /// unknown — to be settled by reconciliation, never recorded as a definite failure.
    /// </summary>
    public void MarkUnknown(string? errorMessage)
    {
        State = NotificationState.Unknown;
        ErrorMessage = errorMessage;
    }

    /// <summary>A scheduled follow-up was cancelled before it went out.</summary>
    public void MarkCancelled(string? providerStatus)
    {
        State = NotificationState.Cancelled;
        if (providerStatus is not null)
        {
            ProviderStatus = providerStatus;
        }
    }

    /// <summary>Refresh the provider-owned outcome from a later read of provider state.</summary>
    public void UpdateProviderOutcome(string? providerSid, string? providerStatus, int? errorCode, string? errorMessage, DateTimeOffset? providerDateSent)
    {
        if (providerSid is not null)
        {
            ProviderSid = providerSid;
        }
        if (providerStatus is not null)
        {
            ProviderStatus = providerStatus;
        }
        if (errorCode is not null)
        {
            ErrorCode = errorCode;
        }
        if (errorMessage is not null)
        {
            ErrorMessage = errorMessage;
        }
        if (providerDateSent is not null)
        {
            ProviderDateSent = providerDateSent;
        }

        // Once we can read a provider outcome, an Unknown row is no longer unknown.
        if (State == NotificationState.Unknown && providerSid is not null)
        {
            State = NotificationState.Sent;
        }
    }

    /// <summary>The message content has been disposed of at the provider and locally.</summary>
    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Body = null;
    }
}
