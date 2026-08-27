using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public enum NotificationType
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3,
    Resend = 4
}

/// <summary>
/// Record of a single SMS sent (or scheduled) for an order, carrying the provider's
/// message identifier and latest known delivery outcome so later requests can act on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Terminal provider statuses; anything else may still change and is worth refreshing.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", "read"
    };

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, int contactNumberId, string toNumber,
        NotificationType type, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        ToNumber = toNumber;
        Type = type;
        Body = body;
        Status = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }
    public string ToNumber { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>Message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    public bool ContentRedacted { get; private set; }

    /// <summary>The provider's message identifier (SM...).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Latest known provider delivery status (queued, sent, delivered, ...).</summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>Caller-supplied key for idempotent operator resends; null for original sends.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>For resends, the notification this one replaces.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SentAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }

    public bool IsTerminal => TerminalStatuses.Contains(Status);

    public void MarkAccepted(string providerMessageSid, string status, DateTimeOffset? scheduledFor = null)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ScheduledFor = scheduledFor;
        SentAt = scheduledFor is null ? DateTimeOffset.UtcNow : null;
    }

    public void MarkSendFailed(string? errorMessage)
    {
        Status = "failed";
        ErrorMessage = errorMessage;
    }

    /// <summary>Advances the stored status; a terminal status is never overwritten by an earlier one.</summary>
    public void UpdateStatus(string status, int? errorCode, string? errorMessage)
    {
        if (IsTerminal && !TerminalStatuses.Contains(status))
        {
            return;
        }

        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
    }

    public void MarkAsResend(string idempotencyKey, int resendOfNotificationId)
    {
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
        Type = NotificationType.Resend;
    }
}
