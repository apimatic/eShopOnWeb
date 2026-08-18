using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or tried to send, or scheduled) to a shopper about one order.
///
/// It carries enough of the state the provider owns — the provider's message identifier
/// (<see cref="ProviderMessageSid"/>) and the current delivery outcome (<see cref="Status"/>) —
/// that a later request can act on it (resend, redact, cancel a scheduled follow-up) and report
/// on it, not only the request that first sent it.
///
/// <see cref="ToNumber"/> is the destination in canonical E.164 form; it is persisted so a resend
/// or reconciliation can act on it, and it is never written to logs.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, NotificationKind kind, string toNumber, string body, bool isScheduled)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        IsScheduled = isScheduled;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper this message is about / belongs to.</summary>
    public string BuyerId { get; private set; }

    /// <summary>Which order-lifecycle moment produced this message.</summary>
    public NotificationKind Kind { get; private set; }

    /// <summary>Destination number in canonical E.164 form. Never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of (redacted).</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio message SID). Null if the send never reached the provider.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (its raw status value), or a local sentinel when the send never reached the provider.</summary>
    public string? Status { get; private set; }

    /// <summary>Provider numeric error code for a failed/undelivered message, when present.</summary>
    public int? ErrorCode { get; private set; }

    /// <summary>Human-readable failure reason, when present. Must never contain the destination number.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>True for the "how did delivery go?" follow-up that is queued with the provider for later.</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>True once the message body has been disposed of at the provider and locally.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key that produced this notification (resend only).</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Records the provider's acceptance of the message: its SID and initial status.</summary>
    public void RecordProviderResult(string? providerMessageSid, string? status, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the send never reached the provider (no SID). The underlying order operation still succeeds.</summary>
    public void RecordSendFailure(string status, string? errorMessage)
    {
        Status = status;
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Refreshes the delivery outcome from the provider.</summary>
    public void UpdateStatus(string? status, int? errorCode, string? errorMessage)
    {
        if (status is not null) Status = status;
        if (errorCode is not null) ErrorCode = errorCode;
        if (errorMessage is not null) ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that a scheduled follow-up was called off before it went out.</summary>
    public void MarkScheduledCancelled(string? status)
    {
        Status = status ?? Status;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Disposes of the message text locally. The fact that a message was sent, and what became of
    /// it (<see cref="ProviderMessageSid"/> / <see cref="Status"/>), survives.
    /// </summary>
    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Attaches the operator-supplied idempotency key that produced this (resend) notification.</summary>
    public void AttachIdempotencyKey(string idempotencyKey)
    {
        IdempotencyKey = idempotencyKey;
    }
}
