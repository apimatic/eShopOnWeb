using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of one SMS message this application sent (or tried to send) about an order. It carries enough of
/// the provider's own state — its message identifier and last-known delivery outcome — that a later request
/// can act on it (resend, cancel, redact) and report on it, not only the request that created it.
///
/// The local row is written BEFORE the provider call (status pending, no SID) and completed after it returns,
/// so a transport failure leaves a row that reconciliation can line up against the provider.
/// <see cref="ToNumber"/> and <see cref="Body"/> are sensitive and must never be logged.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string ownerId, NotificationKind kind, string toNumber, string body,
        string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        OrderId = orderId;
        OwnerId = ownerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>Shopper (username) who owns the order this message is about. Scopes shopper access.</summary>
    public string OwnerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number. Sensitive — never log this.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Sensitive. Null once the content has been disposed.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's message identifier (Twilio SID). Null if the send failed before one was assigned.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's last-known delivery status (wire value, e.g. queued/sent/delivered/undelivered/failed/scheduled/canceled).</summary>
    public string? ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>The provider's own <c>date_sent</c>. This is the clock reconciliation lines both sides up on.</summary>
    public DateTimeOffset? ProviderDateSent { get; private set; }

    /// <summary>True when the send threw before the provider acknowledged it — the operation still succeeded.</summary>
    public bool SendFailed { get; private set; }

    public string? FailureReason { get; private set; }

    /// <summary>True once the message text has been redacted at the provider and locally.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>Caller-supplied idempotency key for an operator resend; unique across notifications.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void RecordSent(string? sid, string? status, int? errorCode, string? errorMessage, DateTimeOffset? dateSent)
    {
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ProviderDateSent = dateSent;
        SendFailed = false;
        FailureReason = null;
    }

    public void RecordSendFailure(string reason)
    {
        SendFailed = true;
        FailureReason = reason;
    }

    public void UpdateProviderState(string? status, int? errorCode, string? errorMessage, DateTimeOffset? dateSent)
    {
        if (ContentDisposed) return; // don't overwrite disposed state
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (dateSent.HasValue) ProviderDateSent = dateSent;
    }

    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
    }
}
