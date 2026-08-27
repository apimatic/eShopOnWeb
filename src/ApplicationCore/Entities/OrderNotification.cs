using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public enum NotificationType
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3,
    Resend = 4
}

/// <summary>
/// Delivery outcomes the provider reports as final; no further transitions are expected.
/// </summary>
public static class NotificationStatus
{
    public const string Pending = "pending";
    public const string Failed = "failed";

    private static readonly string[] Terminal = { "delivered", "undelivered", "failed", "canceled" };

    public static bool IsTerminal(string? status) =>
        status is not null && Array.Exists(Terminal, s => s == status);
}

/// <summary>
/// A record of a single SMS notification sent (or attempted) for an order,
/// carrying the provider's identifier and latest known delivery outcome.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, NotificationType type, string toNumber, string body,
        DateTimeOffset? scheduledFor = null, string? idempotencyKey = null, int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToNumber = toNumber;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>Destination in the provider's canonical (E.164) form. Never write to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>Message text; null once the content has been disposed of.</summary>
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }

    /// <summary>The provider's identifier for the message (null if it never reached the provider).</summary>
    public string? MessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (queued/sent/delivered/...), or a local state.</summary>
    public string Status { get; private set; } = NotificationStatus.Pending;
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? DateSent { get; private set; }

    /// <summary>Caller-supplied key making resend requests idempotent.</summary>
    public string? IdempotencyKey { get; private set; }
    public int? ResendOfNotificationId { get; private set; }

    public void MarkAccepted(string messageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        MessageSid = messageSid;
        Status = providerStatus;
    }

    public void MarkFailed(int? errorCode, string? errorMessage)
    {
        Status = NotificationStatus.Failed;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void ApplyProviderState(string status, int? errorCode, string? errorMessage, DateTimeOffset? dateSent)
    {
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        DateSent = dateSent ?? DateSent;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
