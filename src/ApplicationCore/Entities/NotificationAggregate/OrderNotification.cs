using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Record of a single SMS notification attempt for an order. Carries the provider-owned
/// state (message SID and last known delivery status) so later requests can act on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Status used when the provider never accepted the message (validation/transport failure).
    public const string LocalSendFailedStatus = "send-failed";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, string toNumber, OrderNotificationType type,
        string body, string? idempotencyKey = null, DateTimeOffset? scheduledForUtc = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        Type = type;
        Body = body;
        IdempotencyKey = idempotencyKey;
        ScheduledForUtc = scheduledForUtc;
        Status = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string ToNumber { get; private set; }
    public OrderNotificationType Type { get; private set; }

    /// <summary>Message text. Cleared when the shopper asks for the content to be disposed of.</summary>
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }

    /// <summary>The provider's identifier for the message (null if it never reached the provider).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Last known delivery outcome (provider status, or <see cref="LocalSendFailedStatus"/>).</summary>
    public string Status { get; private set; }
    public string? ErrorDetail { get; private set; }

    /// <summary>Caller-supplied key for idempotent operator re-sends.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedUtc { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledForUtc { get; private set; }

    public void MarkAccepted(string providerMessageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = providerStatus;
        ErrorDetail = null;
    }

    public void MarkSendFailed(string errorDetail)
    {
        Status = LocalSendFailedStatus;
        ErrorDetail = errorDetail;
    }

    public void UpdateProviderStatus(string providerStatus, string? errorDetail = null)
    {
        Status = providerStatus;
        if (!string.IsNullOrEmpty(errorDetail))
        {
            ErrorDetail = errorDetail;
        }
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
