using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of a single SMS notification sent (or attempted) for an order,
/// carrying the provider-owned state (message identifier and delivery outcome)
/// so later requests can act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, string toNumber, NotificationType type, string body,
        string? providerMessageSid, string status, DateTimeOffset? scheduledForUtc = null, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        Type = type;
        Body = body;
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ScheduledForUtc = scheduledForUtc;
        IdempotencyKey = idempotencyKey;
        CreatedUtc = DateTimeOffset.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string ToNumber { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>Message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (null if it never reached the provider).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (queued/sent/delivered/undelivered/scheduled/canceled/...).</summary>
    public string Status { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>Set when the message was queued with the provider for future delivery.</summary>
    public DateTimeOffset? ScheduledForUtc { get; private set; }

    /// <summary>Caller-supplied key for idempotent operator re-sends.</summary>
    public string? IdempotencyKey { get; private set; }

    public bool ContentRedacted { get; private set; }
    public DateTimeOffset CreatedUtc { get; private set; }
    public DateTimeOffset UpdatedUtc { get; private set; }

    public void UpdateProviderState(string status, int? errorCode, string? errorMessage)
    {
        Status = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        UpdatedUtc = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        UpdatedUtc = DateTimeOffset.UtcNow;
    }
}
