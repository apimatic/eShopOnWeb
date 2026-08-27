using System;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of a single SMS notification sent (or attempted) for an order,
/// carrying the provider-owned state (message SID and delivery outcome) so a
/// later request can act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Delivery outcomes reported by the provider that leave nothing further to wait for.
    private static readonly string[] TerminalStatuses = { "delivered", "undelivered", "failed", "canceled" };

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, int contactNumberId, NotificationType type, string body,
        string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Type = type;
        Body = body;
        IdempotencyKey = idempotencyKey;
        Status = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>Message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (null if it never reached the provider).</summary>
    public string? MessageSid { get; private set; }

    /// <summary>Current delivery outcome as last reported by the provider.</summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>Caller-supplied key for idempotent resend requests.</summary>
    public string? IdempotencyKey { get; private set; }

    public bool ContentRedacted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public bool IsInTerminalState => TerminalStatuses.Contains(Status, StringComparer.OrdinalIgnoreCase);

    public void MarkAccepted(string messageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        MessageSid = messageSid;
        UpdateFromProvider(providerStatus, null, null);
    }

    public void UpdateFromProvider(string providerStatus, int? errorCode, string? errorMessage)
    {
        Status = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed(string? errorMessage)
    {
        Status = "failed";
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
