using System;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of one SMS sent (or attempted) for an order, carrying the provider's
/// message identifier and latest known delivery outcome.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Provider statuses that mean the message will not change again.
    public static readonly string[] TerminalStatuses = { "delivered", "undelivered", "failed", "canceled" };
    // Provider statuses from which a scheduled message may still be cancelled.
    public static readonly string[] CancellableStatuses = { "scheduled", "accepted", "queued" };

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, NotificationKind kind, string toNumber, string body,
        DateTimeOffset? scheduledFor = null, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        Status = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string ToNumber { get; private set; }

    /// <summary>Message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's message identifier (SID). Null if the send never reached the provider.</summary>
    public string? MessageSid { get; private set; }

    /// <summary>The provider's latest known delivery outcome (wire value, e.g. queued/delivered).</summary>
    public string Status { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? DateSent { get; private set; }

    /// <summary>Caller-supplied key for operator re-sends; a repeated key must not send twice.</summary>
    public string? IdempotencyKey { get; private set; }

    public bool IsContentRedacted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void MarkAccepted(string messageSid, string providerStatus, DateTimeOffset? dateSent = null)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        MessageSid = messageSid;
        Status = providerStatus;
        DateSent = dateSent;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string? providerStatus, int? errorCode, string? errorMessage)
    {
        Status = string.IsNullOrEmpty(providerStatus) ? "failed" : providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateOutcome(string providerStatus, int? errorCode, string? errorMessage, DateTimeOffset? dateSent = null)
    {
        Guard.Against.NullOrEmpty(providerStatus, nameof(providerStatus));
        Status = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (dateSent.HasValue)
        {
            DateSent = dateSent;
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = null;
        IsContentRedacted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public bool HasTerminalStatus() => TerminalStatuses.Contains(Status);
    public bool IsCancellable() => MessageSid != null && CancellableStatuses.Contains(Status);
}
