using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop sent (or tried to send, or scheduled) about an order.
/// It carries enough of the state the provider owns — the provider message identifier
/// (<see cref="ProviderSid"/>) and the current delivery outcome (<see cref="Status"/>) —
/// that a later request can act on it (resend, cancel a scheduled follow-up, redact content)
/// and report on it, not only the request that first sent it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }

    public Notification(int orderId, string buyerId, NotificationType type, string toNumber, string? body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToNumber = toNumber ?? string.Empty;
        Body = body;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>Owner the notification is about. Scopes shopper reads to their own data.</summary>
    public string BuyerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Destination in E.164 form. Never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>Message text. Cleared when a shopper asks for the content to be disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own identifier for the message (e.g. Twilio message SID).</summary>
    public string? ProviderSid { get; private set; }

    /// <summary>Current delivery outcome. Provider states verbatim, or an application sentinel.</summary>
    public string Status { get; private set; } = NotificationStatus.Queued;

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>True when this message was queued with the provider for future delivery.</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>True once the message content has been disposed of at the provider and locally.</summary>
    public bool ContentDeleted { get; private set; }

    /// <summary>Caller-supplied idempotency key for a resend, so a repeat does not send twice.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>Records the provider's acceptance of the message (immediate or scheduled).</summary>
    public void RecordProviderResult(string sid, string status, int? errorCode, string? errorMessage, bool isScheduled)
    {
        ProviderSid = sid;
        Status = string.IsNullOrEmpty(status) ? NotificationStatus.Sent : status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        IsScheduled = isScheduled;
    }

    /// <summary>Refreshes the delivery outcome from a later provider read.</summary>
    public void UpdateDeliveryState(string status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
        {
            Status = status;
        }
        if (errorCode.HasValue)
        {
            ErrorCode = errorCode;
        }
        if (!string.IsNullOrEmpty(errorMessage))
        {
            ErrorMessage = errorMessage;
        }
    }

    /// <summary>The shopper had no number on file — nothing was sent.</summary>
    public void MarkNoContactNumber()
    {
        Status = NotificationStatus.NoContactNumber;
    }

    /// <summary>The provider rejected the send — the underlying order operation still succeeds.</summary>
    public void MarkSendFailed(int? errorCode, string? errorMessage)
    {
        Status = NotificationStatus.SendFailed;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    /// <summary>The message content has been disposed of; the record of it survives.</summary>
    public void MarkContentDeleted()
    {
        Body = null;
        ContentDeleted = true;
    }

    public void SetIdempotencyKey(string? key)
    {
        if (!string.IsNullOrEmpty(key))
        {
            IdempotencyKey = key;
        }
    }
}
