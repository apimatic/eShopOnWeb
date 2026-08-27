using System;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3,
    Resend = 4
}

/// <summary>
/// A record of a single SMS notification attempt tied to an order. Carries the
/// provider-owned state (message SID, current delivery outcome) so later requests
/// can act on it and report on it. Body is cleared when the shopper asks for the
/// content to be disposed of; the record itself survives.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Terminal delivery outcomes reported by the provider; anything else may still change.
    private static readonly string[] TerminalStatuses = { "delivered", "undelivered", "failed", "canceled" };

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, string toNumber, NotificationKind kind, string body,
        DateTimeOffset? scheduledFor = null, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        Kind = kind;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        Status = scheduledFor.HasValue ? "scheduled" : "pending";
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string ToNumber { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (SM…). Null if the send never got accepted.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Last known delivery outcome: pending/scheduled/queued/sent/delivered/undelivered/failed/canceled.</summary>
    public string Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Caller-supplied key for operator re-sends; a repeat under the same key must not re-send.</summary>
    public string? IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool ContentDisposed { get; private set; }

    public bool IsTerminal => TerminalStatuses.Contains(Status, StringComparer.OrdinalIgnoreCase);

    public bool IsScheduled => string.Equals(Status, "scheduled", StringComparison.OrdinalIgnoreCase);

    public void MarkAccepted(string providerMessageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ApplyProviderStatus(providerStatus, null, null);
    }

    public void MarkSendFailed(string errorMessage, int? errorCode = null)
    {
        Status = "failed";
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderStatus(string providerStatus, int? errorCode, string? errorMessage)
    {
        // Never regress a terminal outcome on stale provider data.
        if (IsTerminal)
        {
            return;
        }

        Status = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
