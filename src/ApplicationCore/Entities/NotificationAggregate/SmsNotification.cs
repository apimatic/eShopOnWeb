using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or tried to send) to a shopper about an order. It carries enough of
/// the state the provider owns — the provider's message identifier (<see cref="ProviderSid"/>) and its
/// current delivery outcome (<see cref="ProviderStatus"/>) — that a later request can act on it
/// (refresh, resend, cancel, redact) and report on it, not only the request that first sent it.
/// The destination number is stored (needed to resend) but is never written to logs.
/// </summary>
public class SmsNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private SmsNotification() { }

    private SmsNotification(string buyerId, int? orderId, NotificationKind kind, string toNumber, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        BuyerId = buyerId;
        OrderId = orderId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        Outcome = NotificationOutcome.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Creates the local record for an outbound message BEFORE the provider is called.</summary>
    public static SmsNotification ForSend(string buyerId, int? orderId, NotificationKind kind, string toNumber, string body)
        => new(buyerId, orderId, kind, toNumber, body);

    /// <summary>Records that a shopper had no number on file, so nothing was sent.</summary>
    public static SmsNotification NotSent(string buyerId, int? orderId, NotificationKind kind, string body)
    {
        var n = new SmsNotification(buyerId, orderId, kind, "(none)", body) { Outcome = NotificationOutcome.NotSent };
        n.Body = body;
        return n;
    }

    public string BuyerId { get; private set; }
    public int? OrderId { get; private set; }
    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number (E.164). Sensitive — never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>Message text. Cleared locally when the content is disposed of.</summary>
    public string? Body { get; private set; }

    public NotificationOutcome Outcome { get; private set; }

    /// <summary>The provider's message identifier (Twilio SID). Null until a message was handed over.</summary>
    public string? ProviderSid { get; private set; }

    /// <summary>The provider's own current status string (e.g. queued, sent, delivered, undelivered, scheduled).</summary>
    public string? ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>Provider event time (when the provider sent the message). The clock reconciliation filters on.</summary>
    public DateTimeOffset? ProviderDateSent { get; private set; }

    /// <summary>For a scheduled follow-up, when it is due to go out.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message text has been redacted at the provider and cleared here.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>When this is a re-send, the notification it re-sent.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkSent(string? sid, string? providerStatus, DateTimeOffset? dateSent)
    {
        ProviderSid = sid;
        ProviderStatus = providerStatus;
        ProviderDateSent = dateSent;
        Outcome = NotificationOutcome.Sent;
    }

    public void MarkScheduled(string? sid, string? providerStatus, DateTimeOffset scheduledSendAt)
    {
        ProviderSid = sid;
        ProviderStatus = providerStatus;
        ScheduledSendAt = scheduledSendAt;
        Outcome = NotificationOutcome.Scheduled;
    }

    public void MarkFailed(int? errorCode, string? errorMessage)
    {
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        Outcome = NotificationOutcome.Failed;
    }

    public void MarkCanceled(string? providerStatus)
    {
        ProviderStatus = providerStatus ?? ProviderStatus;
        Outcome = NotificationOutcome.Canceled;
    }

    public void MarkResendOf(int originalNotificationId) => ResendOfNotificationId = originalNotificationId;

    /// <summary>Applies a freshly fetched provider state to this record.</summary>
    public void RefreshProviderState(string? providerStatus, int? errorCode, string? errorMessage, DateTimeOffset? dateSent)
    {
        if (providerStatus is not null) ProviderStatus = providerStatus;
        if (errorCode is not null) ProviderErrorCode = errorCode;
        if (errorMessage is not null) ProviderErrorMessage = errorMessage;
        if (dateSent is not null) ProviderDateSent = dateSent;

        // Keep the coarse outcome in step with terminal provider states, without ever
        // overriding NotSent/Failed/Scheduled/Canceled with a stale non-terminal value.
        switch (providerStatus)
        {
            case "delivered":
            case "sent":
            case "read":
                if (Outcome is NotificationOutcome.Pending or NotificationOutcome.Sent)
                    Outcome = NotificationOutcome.Sent;
                break;
            case "failed":
            case "undelivered":
                Outcome = NotificationOutcome.Failed;
                break;
            case "canceled":
                Outcome = NotificationOutcome.Canceled;
                break;
        }
    }

    /// <summary>Records that the message content has been disposed of (redacted at provider, cleared here).</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }

    /// <summary>Whether this message is a candidate for an operator re-send (it did not reach the shopper).</summary>
    public bool CanBeResent => Outcome == NotificationOutcome.Failed;

    /// <summary>Whether a queued follow-up can still be called off.</summary>
    public bool IsCancellableFollowUp =>
        Kind == NotificationKind.DeliveryFollowUp && Outcome == NotificationOutcome.Scheduled && ProviderSid is not null;
}
