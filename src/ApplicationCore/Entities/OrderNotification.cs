using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public enum OrderNotificationType
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3
}

/// <summary>
/// A record of a single SMS sent (or attempted) to a shopper about an order.
/// Carries the provider-owned state (message SID and last known delivery
/// outcome) so later requests can act on and report on the message.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Local lifecycle marker used before the provider has accepted the message.
    public const string LocalStatusPending = "pending";
    public const string LocalStatusFailed = "failed";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, string recipientNumber,
        OrderNotificationType type, string body, DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(recipientNumber, nameof(recipientNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        RecipientNumber = recipientNumber;
        Type = type;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        Status = LocalStatusPending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string RecipientNumber { get; private set; }
    public OrderNotificationType Type { get; private set; }

    /// <summary>Message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message.</summary>
    public string? MessageSid { get; private set; }

    /// <summary>Last known delivery outcome reported by the provider.</summary>
    public string Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied key for idempotent operator re-sends.</summary>
    public string? IdempotencyKey { get; private set; }

    public void MarkAccepted(string messageSid, string providerStatus, DateTimeOffset? sentAt)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        MessageSid = messageSid;
        Status = providerStatus;
        SentAt = sentAt;
    }

    public void MarkFailed(string? errorMessage)
    {
        Status = LocalStatusFailed;
        ErrorMessage = errorMessage;
    }

    public void UpdateDeliveryOutcome(string providerStatus, int? errorCode, string? errorMessage, DateTimeOffset? sentAt)
    {
        Status = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        if (sentAt.HasValue)
        {
            SentAt = sentAt;
        }
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
