using System;
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
/// A record of a single SMS sent (or scheduled) for an order, carrying the provider's
/// identifier and latest known delivery outcome so later requests can act on it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private Notification() {}

    public Notification(int orderId, string buyerId, string toNumber, NotificationType type,
        string body, string messageSid, string status, DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null, int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        Type = type;
        Body = body;
        MessageSid = messageSid;
        Status = status;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string ToNumber { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>Message text. Cleared when the content is disposed of; null after redaction.</summary>
    public string? Body { get; private set; }
    public bool BodyRedacted { get; private set; }

    /// <summary>The provider's identifier for the message (e.g. Twilio SM... sid).</summary>
    public string MessageSid { get; private set; }

    /// <summary>The provider's latest known delivery status for the message.</summary>
    public string Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>Set for messages queued with the provider for future delivery.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Caller-supplied key for idempotent resend requests.</summary>
    public string? IdempotencyKey { get; private set; }
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void UpdateStatus(string status, int? errorCode, string? errorMessage)
    {
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RedactBody()
    {
        Body = null;
        BodyRedacted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
