using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of one SMS eShop tried to send about an order. It deliberately carries the
/// state the provider owns — its message identifier (<see cref="ProviderSid"/>) and current
/// delivery outcome (<see cref="Status"/>) — so a later request can act on it (resend, cancel a
/// scheduled follow-up, dispose of content) and report on it, not just the request that sent it.
///
/// The destination number (<see cref="ToNumber"/>) is sensitive and must never be logged or
/// returned by an endpoint; it is retained only so an operator can resend.
/// </summary>
public class SmsNotification : BaseEntity, IAggregateRoot
{
    /// <summary>Local status for a message that was created but not yet handed to the provider.</summary>
    public const string StatusPending = "pending";

    /// <summary>Local status for a message whose send attempt never reached the provider at all.</summary>
    public const string StatusSendError = "send_error";

#pragma warning disable CS8618 // Required by Entity Framework
    private SmsNotification() { }
#pragma warning restore CS8618

    public SmsNotification(string buyerId, int orderId, NotificationKind kind, string toNumber, string body,
        bool isScheduled = false, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));
        BuyerId = buyerId;
        OrderId = orderId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        IsScheduled = isScheduled;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>Owning shopper (JWT username) — used to scope shopper reads to their own orders.</summary>
    public string BuyerId { get; private set; }

    public int OrderId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Canonical E.164 destination. Sensitive: never logged, never returned by an endpoint.</summary>
    public string ToNumber { get; private set; }

    /// <summary>Message text. Null once its content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>Provider's message identifier, once the provider has accepted the message.</summary>
    public string? ProviderSid { get; private set; }

    /// <summary>Latest known delivery outcome (a provider status such as queued/sent/delivered/undelivered/failed/scheduled/canceled).</summary>
    public string Status { get; private set; } = StatusPending;

    public int? ErrorCode { get; private set; }

    /// <summary>True for the delivery follow-up queued with the provider for later; such a message can be called off before it goes out.</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>True once the message content has been disposed of at the shopper's request.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key, set only on operator resends, so a repeat under the same key does not send again.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>The provider accepted the message; record its identifier and initial status.</summary>
    public void RecordAccepted(string providerSid, string status, int? errorCode = null)
    {
        Guard.Against.NullOrEmpty(providerSid, nameof(providerSid));
        ProviderSid = providerSid;
        Status = status;
        ErrorCode = errorCode;
        Touch();
    }

    /// <summary>The send attempt never reached the provider (transport/exception). Keep the record; no SID exists.</summary>
    public void RecordSendError()
    {
        Status = StatusSendError;
        Touch();
    }

    /// <summary>Refresh the delivery outcome from the provider's current view.</summary>
    public void UpdateStatus(string status, int? errorCode = null)
    {
        if (string.IsNullOrEmpty(status))
            return;
        Status = status;
        ErrorCode = errorCode;
        Touch();
    }

    /// <summary>Dispose of the message content locally. Pair with provider-side redaction so the text is unrecoverable, while the record and status survive.</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
