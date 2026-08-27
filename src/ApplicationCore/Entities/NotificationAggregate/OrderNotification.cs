using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Record of a single SMS notification sent (or attempted) for an order, carrying the
/// provider-owned state (message identifier and latest known delivery outcome) so later
/// requests can act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, string toNumber, string? body,
        NotificationType type, DateTimeOffset? scheduledForUtc = null, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        Body = body;
        Type = type;
        ScheduledForUtc = scheduledForUtc;
        IdempotencyKey = idempotencyKey;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string ToNumber { get; private set; }
    public string? Body { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio Message SID).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Latest known delivery outcome reported by the provider.</summary>
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    public DateTimeOffset? ScheduledForUtc { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void MarkAccepted(string providerMessageSid, string? providerStatus)
    {
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = null;
        ProviderErrorMessage = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(int? errorCode, string? errorMessage)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateProviderState(string? providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
