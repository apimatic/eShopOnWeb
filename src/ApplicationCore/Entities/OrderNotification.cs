using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A record of a text message eShop sent (or attempted to send) to a shopper about an order,
/// carrying the provider's identifier and latest known delivery outcome.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, int contactNumberId, NotificationKind kind, string body, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = body;
        IdempotencyKey = idempotencyKey;
        Status = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }
    public bool ContentDisposed { get; private set; }

    /// <summary>The provider's identifier for the message (null if it never reached the provider).</summary>
    public string? MessageSid { get; private set; }

    /// <summary>The provider's latest known delivery outcome (wire value, e.g. queued/sent/delivered/undelivered/failed/scheduled/canceled), or a local state such as pending/send-failed.</summary>
    public string Status { get; private set; } = "pending";
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Caller-supplied idempotency key for operator-initiated resends.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void MarkAccepted(string messageSid, string? status, DateTimeOffset? scheduledFor = null)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        MessageSid = messageSid;
        Status = string.IsNullOrEmpty(status) ? "accepted" : status!;
        ScheduledFor = scheduledFor;
        ErrorCode = null;
        ErrorMessage = null;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed(string? errorMessage)
    {
        Status = "send-failed";
        ErrorMessage = errorMessage;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateDeliveryState(string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
        {
            Status = status!;
        }
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }
}
