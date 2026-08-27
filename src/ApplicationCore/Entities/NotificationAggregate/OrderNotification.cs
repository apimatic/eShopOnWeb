using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Record of a single SMS notification sent (or scheduled) for an order, including the
/// provider-owned state (message identifier and last known delivery outcome) needed to
/// act on the message later and to report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        int contactNumberId,
        NotificationType type,
        string body,
        string? providerMessageSid,
        string status,
        DateTimeOffset? scheduledFor = null,
        string? resendIdempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(status, nameof(status));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Type = type;
        Body = body;
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ScheduledFor = scheduledFor;
        ResendIdempotencyKey = resendIdempotencyKey;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public int ContactNumberId { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>Message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>Provider-owned message identifier (Twilio Message SID).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Last known delivery outcome as reported by the provider.</summary>
    public string Status { get; private set; } = string.Empty;

    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Caller-supplied idempotency key when this notification was produced by a resend.</summary>
    public string? ResendIdempotencyKey { get; private set; }

    public bool ContentRedacted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void UpdateStatus(string status)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
