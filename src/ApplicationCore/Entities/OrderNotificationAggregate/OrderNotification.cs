using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// A single SMS the shop tried to send to a shopper as one of their orders moved.
/// It carries enough of the state the provider owns — the provider's message identifier and the
/// last delivery outcome we observed — that a later request can act on it (resend, cancel, redact)
/// and report on it, not only the request that first sent it.
/// The destination number (<see cref="ToPhoneNumber"/>) is persisted for later action but is never logged.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string toPhoneNumber,
        string? messageBody,
        DateTimeOffset? scheduledSendAt = null,
        string? idempotencyKey = null,
        int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToPhoneNumber = toPhoneNumber;
        MessageBody = messageBody;
        ScheduledSendAt = scheduledSendAt;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
    }

    public int OrderId { get; private set; }

    /// <summary>Owner of the order this notification belongs to (token name claim).</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Canonical E.164 destination. Persisted for later action; never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The message text. Cleared locally once a shopper asks for its content to be disposed of.</summary>
    public string? MessageBody { get; private set; }

    /// <summary>The provider's own identifier for the message, once the provider accepted it.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The last delivery outcome observed from the provider (provider wire value).</summary>
    public string? ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>
    /// Set when the message could not even be handed to the provider (transport/config failure).
    /// The order operation still succeeds; this records that no message went out.
    /// </summary>
    public string? FailureReason { get; private set; }

    /// <summary>When set, the provider has been asked to send this message at a future time.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message content has been disposed of at the provider.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key for an operator resend, if this notification was produced by one.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this notification is a resend, the id of the original notification it re-sent.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedDate { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Record that the provider accepted the message and returned its state.</summary>
    public void RecordProviderResult(string? sid, string? status, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        FailureReason = null;
        Touch();
    }

    /// <summary>Record that the message could not be handed to the provider at all.</summary>
    public void RecordSendFailure(string reason)
    {
        FailureReason = reason;
        Touch();
    }

    /// <summary>Refresh the delivery outcome from a later read of the provider's record.</summary>
    public void UpdateDeliveryState(string? status, int? errorCode, string? errorMessage)
    {
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Content has been disposed of at the provider; drop our local copy but keep the record.</summary>
    public void MarkContentDisposed()
    {
        MessageBody = null;
        ContentRedacted = true;
        Touch();
    }

    private void Touch() => UpdatedDate = DateTimeOffset.UtcNow;
}
