using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or tried to send) to a shopper about one of their orders.
/// It carries enough of the state the provider owns — the message's identifier
/// (<see cref="ProviderMessageSid"/>) and its current delivery outcome (<see cref="Status"/>) —
/// that a later request can act on it (cancel, resend, redact) and report on it, not only the
/// request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>Identity of the shopper the order (and this message) belongs to.</summary>
    public string OwnerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>
    /// The canonical destination number. Persisted so a resend and the reconciliation report
    /// can act on it; it is never written to logs.
    /// </summary>
    public string ToNumber { get; private set; }

    /// <summary>The provider's identifier for the message, once it has been accepted. Null before then / on outright rejection.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Current delivery outcome. Provider wire status verbatim, or a <see cref="NotificationStatus"/> local value.</summary>
    public string Status { get; private set; }

    /// <summary>Provider delivery-failure code on a failed/undelivered message, when known.</summary>
    public int? ErrorCode { get; private set; }

    /// <summary>Provider delivery-failure message, when known. Never contains the destination number.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Set when this notification was produced by an operator resend under a caller-supplied idempotency key.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>True once the message body has been disposed of at the provider. The record and status survive.</summary>
    public bool ContentRedacted { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
    #pragma warning restore CS8618

    public OrderNotification(int orderId, string ownerId, NotificationKind kind, string toNumber, string status)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(status, nameof(status));

        OrderId = orderId;
        OwnerId = ownerId;
        Kind = kind;
        ToNumber = toNumber;
        Status = status;
    }

    /// <summary>Records the provider's identifier and status once the message has been accepted (send or schedule).</summary>
    public void MarkSent(string providerMessageSid, string status)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ErrorCode = null;
        ErrorMessage = null;
    }

    /// <summary>Records that the provider rejected the request outright (no message SID was produced).</summary>
    public void MarkSendFailed(string localStatus, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(localStatus, nameof(localStatus));
        Status = localStatus;
        ErrorMessage = errorMessage;
    }

    /// <summary>Refreshes the delivery outcome from the provider's current view of the message.</summary>
    public void UpdateDeliveryState(string status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void MarkContentRedacted() => ContentRedacted = true;

    public void SetIdempotencyKey(string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        IdempotencyKey = idempotencyKey;
    }
}
