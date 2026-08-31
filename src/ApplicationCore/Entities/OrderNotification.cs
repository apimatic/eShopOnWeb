using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

/// <summary>
/// A record of a single SMS sent (or scheduled) to a shopper about an order.
/// Carries the provider-owned state (message SID and delivery status) so later
/// requests can act on the message and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(
        int orderId,
        string buyerId,
        int? contactNumberId,
        NotificationType type,
        string? body,
        string? providerMessageSid,
        string status,
        int? errorCode,
        DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Type = type;
        Body = body;
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ErrorCode = errorCode;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }

    /// <summary>
    /// The contact number this message went to. Null if the shopper had no number
    /// on file or the number has since been removed. Kept as a plain reference
    /// (no navigation) so removing a contact number never removes history.
    /// </summary>
    public int? ContactNumberId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (SM…).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (queued, sent, delivered, undelivered, failed, scheduled, canceled, …).</summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>Set for messages handed to the provider for future delivery.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Caller-supplied key for operator re-sends; repeats under the same key do not send again.</summary>
    public string? IdempotencyKey { get; private set; }

    public bool ContentRedacted { get; private set; }

    public void UpdateStatus(string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
        ErrorCode = errorCode;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
