using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public enum OrderNotificationType
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3
}

/// <summary>
/// Record of a single SMS sent (or scheduled) for an order, carrying the
/// provider-owned state (message identifier and delivery outcome) so later
/// requests can act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Terminal provider statuses: no further delivery progress is expected.
    public static readonly string[] TerminalStatuses = { "delivered", "undelivered", "failed", "canceled", "send-failed" };

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, string toNumber, OrderNotificationType type,
        string body, string? providerMessageSid, string status, DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(status, nameof(status));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        Type = type;
        Body = body;
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string ToNumber { get; private set; }
    public OrderNotificationType Type { get; private set; }
    public string Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string Status { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedOn { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFor { get; private set; }

    public bool HasTerminalStatus => Array.IndexOf(TerminalStatuses, Status) >= 0;

    public void UpdateProviderStatus(string status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void RedactContent()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }
}
