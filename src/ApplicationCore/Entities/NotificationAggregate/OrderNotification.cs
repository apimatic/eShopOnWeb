using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of a single message the shop raised about an order. It carries enough of the state the
/// provider owns (its message identifier and current delivery outcome) that a later request can act
/// on it (resend, cancel, redact, reconcile) and report on it — not only the request that sent it.
/// The destination number is stored so re-sends can reach it, but it is never written to logs.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, NotificationKind kind, string toNumber)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToNumber = toNumber;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }

    /// <summary>The owning shopper (for owner-scoping).</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Destination E.164 number. Persisted, but never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>Provider message identifier (Twilio <c>sid</c>); null when a send never produced one.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Raw provider delivery status (queued, sent, delivered, undelivered, failed,
    /// scheduled, canceled, ...).</summary>
    public string? ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>True when the message could not be handed to the provider at all (no sid produced).
    /// The business operation still succeeds; this simply records that nothing went out.</summary>
    public bool SendFailed { get; private set; }

    /// <summary>Local reason a send could not be attempted or was rejected outright. Never contains
    /// the destination number.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>True once the message body has been redacted at the provider.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key for the resend that produced this notification.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Records the provider's response to a send/schedule attempt.</summary>
    public void RecordProviderResult(SmsMessageState state)
    {
        Guard.Against.Null(state, nameof(state));
        ProviderMessageSid = state.Sid;
        ProviderStatus = state.Status;
        ProviderErrorCode = state.ErrorCode;
        ProviderErrorMessage = state.ErrorMessage;
        SendFailed = false;
        FailureReason = null;
        Touch();
    }

    /// <summary>Records that the message could not be sent. Does not affect the order operation.</summary>
    public void RecordSendFailure(string reason)
    {
        SendFailed = true;
        FailureReason = reason;
        Touch();
    }

    /// <summary>Refreshes the delivery outcome from a later read of the provider's state.</summary>
    public void RefreshDeliveryState(SmsMessageState state)
    {
        Guard.Against.Null(state, nameof(state));
        ProviderStatus = state.Status;
        ProviderErrorCode = state.ErrorCode;
        ProviderErrorMessage = state.ErrorMessage;
        Touch();
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Touch();
    }

    public void AssignIdempotencyKey(string idempotencyKey)
    {
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>True when this notification is a follow-up still scheduled with the provider and not
    /// yet sent — i.e. it can still be called off.</summary>
    public bool IsPendingSchedule =>
        !string.IsNullOrEmpty(ProviderMessageSid) &&
        string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase);

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
