using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of one message sent (or attempted) to a shopper about an order.
/// Carries the provider's message identifier and last known delivery outcome so
/// later requests can act on it (cancel, resend, redact) and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    /// <summary>App-owned state before the provider has accepted the message.</summary>
    public const string PendingStatus = "pending";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        string toNumber,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor = null,
        int? resendOfId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        Kind = kind;
        Body = body;
        ScheduledFor = scheduledFor;
        ResendOfId = resendOfId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = PendingStatus;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string ToNumber { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public bool BodyRedacted { get; private set; }

    /// <summary>The provider's own identifier for the message (null until accepted).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's last known delivery outcome (wire value), or "pending".</summary>
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public int? ResendOfId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void UpdateProviderState(string? providerMessageSid, string? providerStatus, int? providerErrorCode, string? providerErrorMessage)
    {
        if (!string.IsNullOrEmpty(providerMessageSid))
        {
            ProviderMessageSid = providerMessageSid;
        }
        if (!string.IsNullOrEmpty(providerStatus))
        {
            ProviderStatus = providerStatus;
        }
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkBodyRedacted()
    {
        Body = null;
        BodyRedacted = true;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }
}
