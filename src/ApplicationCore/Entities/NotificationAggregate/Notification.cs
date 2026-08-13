using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or tried to send, or scheduled) about an order.
///
/// A notification carries enough of the state the provider owns — its message identifier
/// (<see cref="ProviderMessageSid"/>) and current delivery outcome (<see cref="ProviderStatus"/>) —
/// that a later request can act on it (resend, cancel a scheduled follow-up, redact) and report on
/// it, not merely the request that first sent it.
///
/// The <see cref="Recipient"/> is a shopper's mobile number and is treated as sensitive: it is
/// never written to logs.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    /// <summary>Local sentinel used when the send could not even be handed to the provider.</summary>
    public const string SendFailedStatus = "send_failed";

    public string OwnerId { get; private set; }
    public int OrderId { get; private set; }

    /// <summary>Destination number in E.164. Sensitive — never logged.</summary>
    public string Recipient { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>The message text. Cleared (null) once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    public bool ContentDisposed { get; private set; }

    // ---- Provider-owned state -------------------------------------------------------------
    /// <summary>The provider's message identifier (Twilio message SID). Null if the send never reached the provider.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome for the message (e.g. queued, sent, delivered, undelivered, failed, scheduled, canceled).</summary>
    public string? ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    // ---- Scheduling -----------------------------------------------------------------------
    /// <summary>True for a message queued with the provider for future delivery (e.g. the delivery follow-up).</summary>
    public bool IsScheduled { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }

    // ---- Resend idempotency ---------------------------------------------------------------
    /// <summary>Caller-supplied idempotency key for the resend that produced this notification, if any.</summary>
    public string? IdempotencyKey { get; private set; }
    /// <summary>The notification this one was a resend of, if any.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

    #pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }

    public Notification(string ownerId, int orderId, string recipient, NotificationType type, string body,
        bool isScheduled = false, DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null, int? resendOfNotificationId = null)
    {
        OwnerId = Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Recipient = Guard.Against.NullOrEmpty(recipient, nameof(recipient));
        Body = Guard.Against.Null(body, nameof(body));
        OrderId = orderId;
        Type = type;
        IsScheduled = isScheduled;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
    }

    /// <summary>Records a successful hand-off to the provider (SID and the provider's initial status).</summary>
    public void RecordSent(string? sid, string? status, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Refreshes the provider-owned delivery state (called after re-reading the provider's record).</summary>
    public void RefreshProviderState(string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(status))
            ProviderStatus = status;
        if (errorCode.HasValue)
            ProviderErrorCode = errorCode;
        if (!string.IsNullOrWhiteSpace(errorMessage))
            ProviderErrorMessage = errorMessage;
    }

    /// <summary>Marks that the message could not be handed to the provider at all.</summary>
    public void MarkSendFailed(string? errorMessage)
    {
        ProviderStatus = SendFailedStatus;
        ProviderErrorMessage = errorMessage;
    }

    /// <summary>Records that a scheduled message was cancelled before it went out.</summary>
    public void MarkCanceled()
    {
        ProviderStatus = "canceled";
    }

    /// <summary>
    /// Disposes of the message text. After this the body is gone locally; callers are responsible for
    /// also redacting it at the provider. The fact a message was sent and what became of it survives.
    /// </summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }

    /// <summary>
    /// True when the provider's outcome says the message did not reach the shopper and so is a
    /// candidate for an operator resend.
    /// </summary>
    public bool DidNotReachShopper()
    {
        if (string.IsNullOrWhiteSpace(ProviderStatus)) return false;
        switch (ProviderStatus.ToLowerInvariant())
        {
            case "failed":
            case "undelivered":
            case "canceled":
            case SendFailedStatus:
                return true;
            default:
                return false;
        }
    }

    /// <summary>True while the message is still in flight and worth re-reading from the provider.</summary>
    public bool IsInNonTerminalState()
    {
        if (string.IsNullOrWhiteSpace(ProviderStatus)) return false;
        switch (ProviderStatus.ToLowerInvariant())
        {
            case "delivered":
            case "undelivered":
            case "failed":
            case "canceled":
            case SendFailedStatus:
                return false; // terminal
            default:
                return true;  // queued, sending, sent, accepted, scheduled, receiving, read...
        }
    }
}
