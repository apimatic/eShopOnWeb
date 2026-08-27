using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public enum NotificationType
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3
}

/// <summary>
/// A single SMS sent (or scheduled) for an order, tracking the provider's
/// message identifier and latest known delivery outcome.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Terminal delivery outcomes reported by the provider; once reached, no further
    // status refresh is needed and a late earlier status must not overwrite them.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", "read"
    };

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, int? contactNumberId, string toNumber,
        NotificationType type, string body, DateTimeOffset? scheduledFor = null, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        ToNumber = toNumber;
        Type = type;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        Status = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string ToNumber { get; private set; }
    public NotificationType Type { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool ContentRedacted { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public bool IsTerminal => TerminalStatuses.Contains(Status);
    public bool IsScheduled => string.Equals(Status, "scheduled", StringComparison.OrdinalIgnoreCase);

    public void RecordProviderAcceptance(string providerMessageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        Guard.Against.NullOrEmpty(providerStatus, nameof(providerStatus));

        ProviderMessageSid = providerMessageSid;
        Status = providerStatus;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateStatus(string providerStatus, int? errorCode, string? errorMessage)
    {
        if (IsTerminal)
        {
            return;
        }

        Status = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed(string errorMessage)
    {
        Status = "failed";
        ErrorMessage = errorMessage;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }
}
