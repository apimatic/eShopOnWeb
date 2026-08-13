using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS produced as an order moves. It carries enough of the state the provider owns
/// — the provider's message identifier and the current delivery outcome — that a later
/// request can act on it (resend, dispose content) and report on it (my-orders, notifications,
/// reconciliation), not only the request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, NotificationKind kind, string recipientNumber, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(recipientNumber, nameof(recipientNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        RecipientNumber = recipientNumber;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>Owning shopper (the order's buyer). Used to scope shopper-facing reads.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Canonical E.164 destination. Never written to logs.</summary>
    public string RecipientNumber { get; private set; }

    /// <summary>
    /// The message text. Kept so a resend can reproduce it; set to null once the content has
    /// been disposed at the shopper's request.
    /// </summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own identifier for the message (Twilio message SID), if accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Last known provider delivery status (e.g. queued, sent, delivered, undelivered, failed, canceled).</summary>
    public string? ProviderStatus { get; private set; }

    /// <summary>Provider error code for a failed/undelivered message, if any.</summary>
    public int? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>True for the delivery follow-up that was queued with the provider for later.</summary>
    public bool IsScheduled { get; private set; }

    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once a scheduled message has been called off before it went out.</summary>
    public bool IsCancelled { get; private set; }

    /// <summary>True once the content has been disposed (redacted at the provider and cleared here).</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>Caller-supplied idempotency key for the resend that produced this record, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void MarkScheduled(DateTimeOffset sendAt)
    {
        IsScheduled = true;
        ScheduledSendAt = sendAt;
    }

    public void SetIdempotencyKey(string idempotencyKey)
    {
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>Record the provider's response to the send/schedule attempt.</summary>
    public void RecordDispatch(string? providerMessageSid, string? status, int? errorCode, string? errorMessage)
    {
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>Refresh the delivery outcome from a later read of the provider's record.</summary>
    public void UpdateDeliveryState(string? status, int? errorCode, string? errorMessage)
    {
        if (status is not null)
        {
            ProviderStatus = status;
        }
        if (errorCode is not null)
        {
            ErrorCode = errorCode;
        }
        if (errorMessage is not null)
        {
            ErrorMessage = errorMessage;
        }
    }

    public void MarkCancelled(string? status = null)
    {
        IsCancelled = true;
        if (status is not null)
        {
            ProviderStatus = status;
        }
    }

    /// <summary>
    /// Clear the locally held content. The provider-side redaction is performed by the caller;
    /// the fact a message was sent and what became of it (sid, status) survives.
    /// </summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }

    /// <summary>Whether the message failed to reach the shopper and is therefore eligible for resend.</summary>
    public bool DidNotReachRecipient()
    {
        if (ProviderMessageSid is null)
        {
            return true;
        }
        if (string.IsNullOrEmpty(ProviderStatus))
        {
            return false;
        }
        return ProviderStatus.Equals("failed", StringComparison.OrdinalIgnoreCase)
            || ProviderStatus.Equals("undelivered", StringComparison.OrdinalIgnoreCase)
            || ProviderStatus.Equals("canceled", StringComparison.OrdinalIgnoreCase);
    }
}
