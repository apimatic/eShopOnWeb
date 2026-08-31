using System;
using System.Linq;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public enum NotificationType
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3
}

/// <summary>
/// Records a single SMS notification attempt for an order, including the
/// provider-owned state (message identifier and delivery outcome) so that a
/// later request can act on it (cancel, resend, redact, reconcile).
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Delivery outcomes that will never change again; no point re-polling the provider.
    private static readonly string[] TerminalStatuses = { "delivered", "undelivered", "failed", "canceled" };

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, int contactNumberId, string toNumber,
        NotificationType type, string body, DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null, int? resendOfNotificationId = null)
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
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }

    /// <summary>Destination in provider-canonical (E.164) form. Never written to logs.</summary>
    public string ToNumber { get; private set; }

    public NotificationType Type { get; private set; }
    public string Body { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio Message SID).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (queued, sent, delivered, ...).</summary>
    public string ProviderStatus { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Caller-supplied key guaranteeing a resend is executed at most once.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Set when this record is the product of re-sending another notification.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public bool ContentRedacted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public bool IsTerminal => TerminalStatuses.Contains(ProviderStatus);
    public bool IsScheduled => ProviderStatus == "scheduled";

    public void MarkAccepted(string providerMessageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        Touch();
    }

    public void MarkRejected(string? errorCode, string? errorMessage)
    {
        ProviderStatus = "failed";
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    public void UpdateProviderStatus(string providerStatus, string? errorCode, string? errorMessage)
    {
        ProviderStatus = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    public void MarkCanceled()
    {
        ProviderStatus = "canceled";
        Touch();
    }

    public void RedactContent()
    {
        Body = string.Empty;
        ContentRedacted = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
