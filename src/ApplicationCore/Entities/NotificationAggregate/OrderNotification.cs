using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS notification sent (or attempted) for an order, together with the
/// provider-owned state (message SID, current delivery outcome) needed to act on it later.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Local status used when the provider never accepted the message.
    public const string SendFailedStatus = "send_failed";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, NotificationType type, string toNumber, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToNumber = toNumber;
        Body = body;
        Status = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationType Type { get; private set; }
    public string ToNumber { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string Status { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public void MarkAccepted(string providerMessageSid, string providerStatus, DateTimeOffset? scheduledFor = null)
    {
        ProviderMessageSid = providerMessageSid;
        Status = providerStatus;
        ScheduledFor = scheduledFor;
        ErrorMessage = null;
        Touch();
    }

    public void MarkSendFailed(string errorMessage)
    {
        Status = SendFailedStatus;
        ErrorMessage = errorMessage;
        Touch();
    }

    public void UpdateStatus(string providerStatus, string? errorMessage = null)
    {
        Status = providerStatus;
        if (!string.IsNullOrEmpty(errorMessage))
        {
            ErrorMessage = errorMessage;
        }
        Touch();
    }

    public void AssignIdempotencyKey(string idempotencyKey)
    {
        IdempotencyKey = idempotencyKey;
        Touch();
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        Touch();
    }

    public bool IsScheduled => Status == "scheduled";

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
