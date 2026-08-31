using System;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS notification sent (or attempted) for an order, carrying the
/// provider-owned state (message identifier and delivery outcome) needed to
/// act on and report on the message later.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Delivery outcomes reported by the provider that will never change again.
    private static readonly string[] TerminalStatuses = { "delivered", "undelivered", "failed", "canceled" };

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, string toNumber, string body, NotificationType type, DateTimeOffset? scheduledFor = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        Body = body;
        Type = type;
        ScheduledFor = scheduledFor;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string ToNumber { get; private set; }

    /// <summary>Message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio Message SID).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome for the message.</summary>
    public string ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>Caller-supplied key for idempotent resend requests. Set only on messages produced by a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset? ScheduledFor { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public bool IsInTerminalState => TerminalStatuses.Contains(ProviderStatus);

    public void MarkAccepted(string providerMessageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateProviderStatus(string providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkIdempotencyKey(string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        IdempotencyKey = idempotencyKey;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
