using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop tried to send to a shopper about one of their orders.
///
/// It carries enough of the state the provider owns — the provider's message identifier
/// (<see cref="MessageSid"/>) and the current delivery outcome (<see cref="Status"/>,
/// <see cref="ErrorCode"/>) — that a later request can act on it (resend, cancel a scheduled
/// send, dispose of its content) and report on it, not only the request that first sent it.
///
/// The <see cref="ToNumber"/> is the shopper's own number and is sensitive: it is stored so the
/// message can be reconciled and re-sent, but it is never written to logs.
/// </summary>
public class SmsNotification : BaseEntity, IAggregateRoot
{
    // eShop-side sentinel statuses (everything else mirrors the provider's own status string).

    /// <summary>The record was created but nothing has been submitted to the provider yet.</summary>
    public const string StatusNotSent = "not_sent";

    /// <summary>The provider never accepted the message (transport/credentials error on our side).</summary>
    public const string StatusSubmissionFailed = "submission_failed";

    /// <summary>The destination number was no longer on file, so we deliberately did not send.</summary>
    public const string StatusRecipientRemoved = "recipient_removed";

    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", "read", "partially_delivered",
        StatusSubmissionFailed, StatusRecipientRemoved
    };

    public int OrderId { get; private set; }

    /// <summary>The shopper the message is about — used to scope reads to the caller's own data.</summary>
    public string OwnerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>The destination in E.164 form. Sensitive — never log this.</summary>
    public string ToNumber { get; private set; }

    /// <summary>
    /// The text that was sent. Cleared when a shopper asks for the content to be disposed of
    /// (see <see cref="MarkContentDisposed"/>); the record and its outcome survive.
    /// </summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message, once it has accepted it.</summary>
    public string? MessageSid { get; private set; }

    /// <summary>The current delivery outcome as owned by the provider (or an eShop-side sentinel).</summary>
    public string Status { get; private set; } = StatusNotSent;

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>True when this is a future (scheduled) send queued with the provider.</summary>
    public bool IsScheduled { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>
    /// The caller-supplied idempotency key for a resend, so a repeat under the same key returns
    /// the same message instead of sending a second one.
    /// </summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>True once the message body has been disposed of at the provider and locally.</summary>
    public bool ContentDisposed { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    #pragma warning disable CS8618 // Required by Entity Framework
    private SmsNotification() { }
    #pragma warning restore CS8618

    public SmsNotification(int orderId, string ownerId, NotificationType type, string toNumber, string body,
        bool isScheduled = false, DateTimeOffset? scheduledFor = null, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        OwnerId = ownerId;
        Type = type;
        ToNumber = toNumber;
        Body = body;
        IsScheduled = isScheduled;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>True once the outcome is settled and no further provider polling is worthwhile.</summary>
    public bool IsTerminal => TerminalStatuses.Contains(Status);

    /// <summary>True when the provider accepted the message and gave us a SID to act on later.</summary>
    public bool WasAcceptedByProvider => !string.IsNullOrEmpty(MessageSid);

    /// <summary>Record the outcome of submitting the message to the provider.</summary>
    public void ApplyProviderResult(string? messageSid, string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(messageSid))
            MessageSid = messageSid;

        Status = string.IsNullOrEmpty(status) ? StatusSubmissionFailed : status!;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>The provider never accepted the message; the underlying order operation still succeeds.</summary>
    public void MarkSubmissionFailed(string? reason)
    {
        Status = StatusSubmissionFailed;
        ErrorMessage = reason;
        Touch();
    }

    /// <summary>The destination is no longer registered to the shopper, so nothing was sent.</summary>
    public void MarkRecipientRemoved()
    {
        Status = StatusRecipientRemoved;
        Touch();
    }

    /// <summary>Refresh the delivery outcome from a fresh read of the provider's record.</summary>
    public void ApplyDeliveryState(string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
            Status = status!;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>The scheduled send was called off with the provider before it went out.</summary>
    public void MarkCanceled()
    {
        Status = "canceled";
        Touch();
    }

    /// <summary>Dispose of the message content locally; the fact of the message and its outcome remain.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
