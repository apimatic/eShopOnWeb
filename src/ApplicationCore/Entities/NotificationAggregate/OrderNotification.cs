using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of a text message sent (or attempted) for an order, carrying the provider's
/// own state for the message (its identifier and last known delivery outcome) so later
/// requests can act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string ToNumber { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>The provider's message identifier. Null when the provider never accepted the message.</summary>
    public string? MessageSid { get; private set; }

    /// <summary>The message text. Cleared permanently when the content is disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's last known delivery outcome (wire value, e.g. queued/sent/delivered/undelivered/failed/scheduled/canceled).</summary>
    public string LastKnownStatus { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFor { get; private set; }
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key; set on notifications produced by an operator resend.</summary>
    public string? IdempotencyKey { get; private set; }

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, string toNumber, NotificationType type,
        string? messageSid, string? body, string lastKnownStatus,
        DateTimeOffset? scheduledFor = null, string? idempotencyKey = null,
        int? errorCode = null, string? errorMessage = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(lastKnownStatus, nameof(lastKnownStatus));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        Type = type;
        MessageSid = messageSid;
        Body = body;
        LastKnownStatus = lastKnownStatus;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void UpdateStatus(string status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        LastKnownStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
