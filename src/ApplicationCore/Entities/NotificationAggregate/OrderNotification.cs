using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of one SMS sent (or attempted) for an order, carrying the provider-owned
/// state (message identifier and latest known delivery outcome) so later requests can
/// act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, string toNumber, string body,
        OrderNotificationKind kind, DateTimeOffset? scheduledFor = null, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        Body = body;
        Kind = kind;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        Status = OrderNotificationStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string ToNumber { get; private set; }

    /// <summary>Message text. Set to null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    public OrderNotificationKind Kind { get; private set; }

    /// <summary>The provider's own identifier for the message; null if the provider never accepted it.</summary>
    public string? MessageSid { get; private set; }

    /// <summary>Latest known delivery outcome (provider status wire value, or a local failure marker).</summary>
    public string Status { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Caller-supplied key for operator re-sends; a repeated key must not produce a second send.</summary>
    public string? IdempotencyKey { get; private set; }

    public bool ContentRedacted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public void MarkAccepted(string messageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        MessageSid = messageSid;
        Status = providerStatus;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed(string? providerErrorMessage, int? providerErrorCode = null)
    {
        Status = OrderNotificationStatus.SendFailed;
        ProviderErrorMessage = providerErrorMessage;
        ProviderErrorCode = providerErrorCode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateProviderState(string providerStatus, int? errorCode, string? errorMessage)
    {
        Status = providerStatus;
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
