using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or scheduled) to a shopper as one of their orders moved.
///
/// The record carries enough of the state the messaging provider owns — its identifier
/// (<see cref="ProviderMessageId"/>) and current delivery outcome (<see cref="Status"/>,
/// <see cref="ErrorCode"/>) — that a later request can both act on the message (cancel a
/// scheduled follow-up, resend a failed one, dispose of its content) and report on it,
/// not just the request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { } // EF only

    public OrderNotification(int orderId, string ownerId, NotificationKind kind)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));

        OrderId = orderId;
        OwnerId = ownerId;
        Kind = kind;
        Status = NotificationStatuses.Pending;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>Identity (username / email) of the shopper the message is for. Used for scoping.</summary>
    public string OwnerId { get; private set; } = default!;

    public NotificationKind Kind { get; private set; }

    /// <summary>
    /// The provider's canonical destination number for this message. Personal data —
    /// stored for reconciliation and never written to logs or returned raw by the API.
    /// </summary>
    public string? ToNumber { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio Message SID), once created.</summary>
    public string? ProviderMessageId { get; private set; }

    /// <summary>The provider's current delivery status for the message.</summary>
    public string Status { get; private set; } = NotificationStatuses.Pending;

    public int? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>The message text. Cleared once the shopper asks for its content to be disposed of.</summary>
    public string? Body { get; private set; }

    public bool ContentRedacted { get; private set; }

    /// <summary>For a scheduled follow-up, when the provider is due to send it.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>
    /// Idempotency key supplied by the caller when this message was produced by an operator
    /// re-send. A repeat request under the same key returns this record instead of sending again.
    /// </summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? UpdatedAt { get; private set; }

    public void SetIdempotencyKey(string key)
    {
        Guard.Against.NullOrEmpty(key, nameof(key));
        IdempotencyKey = key;
    }

    /// <summary>Record the text and destination before an attempt to send is made.</summary>
    public void SetContent(string toNumber, string body)
    {
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        ToNumber = toNumber;
        Body = body;
    }

    /// <summary>The provider accepted the message for immediate delivery.</summary>
    public void MarkAccepted(string providerMessageId, string status)
    {
        Guard.Against.NullOrEmpty(providerMessageId, nameof(providerMessageId));
        ProviderMessageId = providerMessageId;
        Status = string.IsNullOrEmpty(status) ? NotificationStatuses.Queued : status;
        ErrorCode = null;
        ErrorMessage = null;
        Touch();
    }

    /// <summary>The provider accepted the message for future delivery.</summary>
    public void MarkScheduled(string providerMessageId, string status, DateTimeOffset scheduledFor)
    {
        Guard.Against.NullOrEmpty(providerMessageId, nameof(providerMessageId));
        ProviderMessageId = providerMessageId;
        Status = string.IsNullOrEmpty(status) ? NotificationStatuses.Scheduled : status;
        ScheduledFor = scheduledFor;
        Touch();
    }

    /// <summary>Refresh the delivery outcome from the provider's own record.</summary>
    public void UpdateDeliveryState(string status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
        {
            Status = status;
        }
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>The message could not even be handed to the provider (no identifier was issued).</summary>
    public void MarkSendFailed(string errorMessage)
    {
        Status = NotificationStatuses.Failed;
        ErrorMessage = errorMessage;
        Touch();
    }

    public void MarkCanceled()
    {
        Status = NotificationStatuses.Canceled;
        Touch();
    }

    /// <summary>The shopper asked for the content to be disposed of; drop the local copy of the text.</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
