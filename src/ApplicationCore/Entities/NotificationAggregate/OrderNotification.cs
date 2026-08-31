using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Record of a single SMS sent (or attempted) to a shopper about an order.
/// Carries the provider's identifier (MessageSid) and latest known delivery outcome,
/// so a later request can act on it (cancel, resend, redact) and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}
    #pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        int? contactNumberId,
        string toNumber,
        NotificationKind kind,
        string? body,
        string? messageSid,
        string status,
        DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null,
        int? errorCode = null,
        string? errorMessage = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(status, nameof(status));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        ToNumber = toNumber;
        Kind = kind;
        Body = body;
        MessageSid = messageSid;
        Status = status;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }

    /// <summary>The registered contact number this message went to, if it still exists.</summary>
    public int? ContactNumberId { get; private set; }

    /// <summary>Canonical E.164 destination at the time of sending.</summary>
    public string ToNumber { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The provider's own identifier for the message; null if the send never reached the provider.</summary>
    public string? MessageSid { get; private set; }

    /// <summary>Latest known delivery outcome (provider wire values: queued, sent, delivered, undelivered, failed, scheduled, canceled; or local values such as send-failed).</summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    public bool ContentRedacted { get; private set; }

    /// <summary>Set for messages queued with the provider for future delivery.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Caller-supplied key for operator-initiated resends; repeats under the same key must not send again.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedOn { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUpdatedOn { get; private set; } = DateTimeOffset.UtcNow;

    public void UpdateDeliveryOutcome(string status, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        LastUpdatedOn = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        LastUpdatedOn = DateTimeOffset.UtcNow;
    }
}
