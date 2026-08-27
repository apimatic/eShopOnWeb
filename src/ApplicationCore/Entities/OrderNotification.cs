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
/// A record of a single SMS notification sent (or attempted) for an order,
/// carrying the provider-owned state (message identifier and delivery outcome)
/// so later requests can act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Local status used when the provider never accepted the message.
    public const string SendFailedStatus = "send-failed";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, int contactNumberId, NotificationType type, string body,
        DateTimeOffset? scheduledFor = null, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Type = type;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>Message text. Cleared when the content is disposed of.</summary>
    public string? Body { get; private set; }
    public bool ContentDisposed { get; private set; }

    /// <summary>Provider-owned identifier of the message (null if the provider never accepted it).</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Last known provider delivery outcome (queued/sent/delivered/undelivered/failed/scheduled/canceled).</summary>
    public string Status { get; private set; } = "pending";
    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Caller-supplied idempotency key, set on notifications produced by a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    public void MarkAccepted(string providerMessageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = providerStatus;
        ErrorMessage = null;
    }

    public void MarkSendFailed(string error)
    {
        Status = SendFailedStatus;
        ErrorMessage = error;
    }

    public void UpdateProviderStatus(string providerStatus, string? error)
    {
        Status = providerStatus;
        ErrorMessage = error;
    }

    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }
}
