using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// One SMS the shop sent (or tried to send) about an order. It carries enough of the state the
/// provider owns — its message identifier and current delivery outcome — that a later request can
/// act on it (cancel, resend, redact) and report on it, not only the request that created it.
/// The delivery lifecycle:
/// <list type="bullet">
///   <item><c>pending</c> — row written before the provider call.</item>
///   <item>a provider status (<c>queued/accepted/sending/sent/delivered/undelivered/failed/scheduled/canceled…</c>) after it.</item>
///   <item><c>send-error</c> — the provider call itself failed; the local record still survives.</item>
/// </list>
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    public const string StatusPending = "pending";
    public const string StatusSendError = "send-error";
    public const string StatusCanceled = "canceled";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(string ownerId, int orderId, NotificationKind kind, string toNumber,
        string body, bool isScheduledFollowUp = false, int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OwnerId = ownerId;
        OrderId = orderId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        IsScheduledFollowUp = isScheduledFollowUp;
        ResendOfNotificationId = resendOfNotificationId;
        Status = StatusPending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Owner (buyer) the message is about — used to scope shopper reads.</summary>
    public string OwnerId { get; private set; }
    public int OrderId { get; private set; }
    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number (canonical E.164). Persisted so a resend knows where to go; never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>Message text. Nulled out once the content has been disposed of at the provider.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's message identifier (SID), once a send has been accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Provider delivery status (or a local pseudo-status; see the class remarks).</summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>Provider's own <c>date_sent</c>, when known — the clock reconciliation filters on.</summary>
    public DateTimeOffset? ProviderDateSent { get; private set; }

    public bool ContentRedacted { get; private set; }
    public bool IsScheduledFollowUp { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Record the provider's acknowledgement of a send (its SID + status).</summary>
    public void RecordSent(string providerMessageSid, string providerStatus, int? errorCode,
        string? errorMessage, DateTimeOffset? providerDateSent)
    {
        ProviderMessageSid = providerMessageSid;
        Status = string.IsNullOrWhiteSpace(providerStatus) ? StatusPending : providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ProviderDateSent = providerDateSent;
    }

    /// <summary>Record that the provider call failed; the local record still survives.</summary>
    public void RecordSendFailure(string? errorMessage)
    {
        Status = StatusSendError;
        ErrorMessage = errorMessage;
    }

    /// <summary>Refresh from a later fetch of the provider's current view.</summary>
    public void RecordStatus(string providerStatus, int? errorCode, string? errorMessage,
        DateTimeOffset? providerDateSent)
    {
        if (!string.IsNullOrWhiteSpace(providerStatus)) Status = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        if (providerDateSent.HasValue) ProviderDateSent = providerDateSent;
    }

    public void MarkCanceled() => Status = StatusCanceled;

    /// <summary>Content disposed of at the provider: drop the local copy of the text too.</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    /// <summary>True when the provider's outcome is a non-delivery — the resend-eligible states.</summary>
    public bool DidNotReachRecipient =>
        Status.Equals("failed", StringComparison.OrdinalIgnoreCase)
        || Status.Equals("undelivered", StringComparison.OrdinalIgnoreCase)
        || Status.Equals(StatusSendError, StringComparison.OrdinalIgnoreCase);

    /// <summary>True while a scheduled follow-up has not yet gone out and can still be called off.</summary>
    public bool IsPendingFollowUp =>
        IsScheduledFollowUp && ProviderMessageSid is not null
        && (Status.Equals("scheduled", StringComparison.OrdinalIgnoreCase)
            || Status.Equals("accepted", StringComparison.OrdinalIgnoreCase)
            || Status.Equals("queued", StringComparison.OrdinalIgnoreCase));
}
