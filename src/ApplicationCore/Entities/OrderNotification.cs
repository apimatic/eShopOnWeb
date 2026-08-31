using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3,
    Resend = 4
}

/// <summary>
/// A record of one SMS notification sent (or attempted) for an order, carrying the
/// provider-owned state (message identifier and latest known delivery outcome) so a
/// later request can act on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Local statuses used when the provider never accepted the message.
    public const string StatusPending = "pending";
    public const string StatusSendFailed = "send-failed";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, int? contactNumberId, string toNumber,
        NotificationKind kind, string body, DateTimeOffset? scheduledFor = null, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        ToNumber = toNumber;
        Kind = kind;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        Status = StatusPending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string ToNumber { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string Status { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void MarkAccepted(string providerMessageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = providerStatus;
    }

    public void MarkSendFailed(string? providerErrorMessage)
    {
        Status = StatusSendFailed;
        ProviderErrorMessage = providerErrorMessage;
    }

    public void UpdateProviderOutcome(string providerStatus, int? errorCode, string? errorMessage)
    {
        Status = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void RedactContent()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }
}
