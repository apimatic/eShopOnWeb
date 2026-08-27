using System;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of a single SMS notification sent (or attempted) for an order, carrying the
/// provider-owned state (message SID and last known delivery outcome) so later requests
/// can act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Terminal provider statuses; anything else is refreshed from the provider on read.
    private static readonly string[] TerminalStatuses = { "delivered", "undelivered", "failed", "canceled", "read" };

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, int contactNumberId, string toNumber,
        NotificationType notificationType, string body, DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null, int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        ToNumber = toNumber;
        NotificationType = notificationType;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
        Status = "pending";
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }

    /// <summary>Snapshot of the destination (E.164) at send time.</summary>
    public string ToNumber { get; private set; }

    public NotificationType NotificationType { get; private set; }

    /// <summary>Message text. Cleared when the content is disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (null if it never reached the provider).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Last known provider delivery outcome (queued/sent/delivered/undelivered/failed/scheduled/canceled...).</summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>Set for provider-scheduled follow-up messages.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Caller-supplied key for operator re-sends; guarantees a repeated request does not send twice.</summary>
    public string? IdempotencyKey { get; private set; }

    public int? ResendOfNotificationId { get; private set; }

    public bool ContentRedacted { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsTerminal => TerminalStatuses.Contains(Status);

    public void MarkProviderAccepted(string providerMessageSid, string status, DateTimeOffset? scheduledFor = null)
    {
        ProviderMessageSid = providerMessageSid;
        Status = status;
        if (scheduledFor.HasValue)
        {
            ScheduledFor = scheduledFor;
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkFailed(string? errorMessage, int? errorCode = null)
    {
        Status = "failed";
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateProviderStatus(string status, int? errorCode, string? errorMessage)
    {
        // Never regress a terminal status with a stale earlier one.
        if (IsTerminal && !TerminalStatuses.Contains(status))
        {
            return;
        }

        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
