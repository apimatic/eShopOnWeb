using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public enum NotificationKind
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCancelled
}

/// <summary>
/// Record of a single SMS sent (or attempted) for an order, carrying the
/// provider-owned state (message SID and current delivery outcome) so later
/// requests can act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, int contactNumberId, string toNumber, NotificationKind kind, string body,
        DateTimeOffset? scheduledFor = null, int? resendOfNotificationId = null, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        ToNumber = toNumber;
        Kind = kind;
        Body = body;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        Status = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }

    /// <summary>The destination the message went to. Never write to logs.</summary>
    public string ToNumber { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (SM.../MM...).</summary>
    public string? MessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome for the message.</summary>
    public string Status { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public bool ContentDisposed { get; private set; }

    /// <summary>Set when this notification was produced by an operator resend.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>Caller-supplied idempotency key of the resend that produced this notification.</summary>
    public string? IdempotencyKey { get; private set; }

    public void MarkSent(string messageSid, string status)
    {
        MessageSid = messageSid;
        Status = status;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed(string? errorMessage)
    {
        Status = "failed";
        ProviderErrorMessage = errorMessage;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateDeliveryOutcome(string status, int? errorCode, string? errorMessage)
    {
        Status = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }
}
