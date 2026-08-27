using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Records a single SMS sent (or attempted) to a shopper about an order,
/// together with the provider-owned state (message identifier and delivery
/// outcome) needed to act on it and report on it later.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Delivery outcomes the provider considers final; once reached, no further
    // status refresh is needed.
    private static readonly string[] TerminalStatuses =
        { "delivered", "undelivered", "failed", "canceled", "read" };

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, NotificationType type,
        string? messageSid, string? body, string status,
        int? errorCode = null, string? errorMessage = null,
        DateTimeOffset? scheduledFor = null, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        MessageSid = messageSid;
        Body = body;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>The provider's identifier for the message (null if it never reached the provider).</summary>
    public string? MessageSid { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's current delivery outcome for the message.</summary>
    public string Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFor { get; private set; }
    public bool IsContentRedacted { get; private set; }

    /// <summary>Caller-supplied key for operator re-sends, used to suppress duplicates.</summary>
    public string? IdempotencyKey { get; private set; }

    public bool HasTerminalStatus =>
        Array.Exists(TerminalStatuses, s => string.Equals(s, Status, StringComparison.OrdinalIgnoreCase));

    public void UpdateStatus(string status, int? errorCode = null, string? errorMessage = null)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        // Never regress a terminal outcome with stale, earlier state.
        if (HasTerminalStatus &&
            !Array.Exists(TerminalStatuses, s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void MarkContentRedacted()
    {
        Body = null;
        IsContentRedacted = true;
    }
}
