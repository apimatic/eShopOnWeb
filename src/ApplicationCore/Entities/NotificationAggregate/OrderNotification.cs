using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string? providerMessageSid,
        string? providerStatus,
        string body,
        DateTimeOffset? scheduledSendAt = null,
        int? sourceNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        Body = body;
        ScheduledSendAt = scheduledSendAt;
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void ApplyProviderState(string? status, int? errorCode, string? errorMessage)
    {
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void RedactContent()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }

    public bool IsPendingFollowUp()
    {
        if (Kind != NotificationKind.DeliveryFollowUp)
            return false;
        if (string.IsNullOrEmpty(ProviderMessageSid))
            return false;

        return ProviderStatus is "scheduled" or "queued" or "accepted" or null;
    }

    public bool DidNotReachShopper()
    {
        return ProviderStatus is "failed" or "undelivered" or "canceled";
    }
}
