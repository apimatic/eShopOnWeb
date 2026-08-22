using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string body,
        string destinationNumber)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationNumber = destinationNumber;
        CreatedAt = DateTimeOffset.UtcNow;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? SendFailure { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSyncedAt { get; private set; }

    public void RecordProviderAcceptance(string messageSid, string status, DateTimeOffset? scheduledFor)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        ProviderMessageSid = messageSid;
        ProviderStatus = status;
        ScheduledFor = scheduledFor;
        SendFailure = null;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void RecordSendFailure(string reason)
    {
        SendFailure = reason;
        ProviderStatus = "failed_to_submit";
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderSnapshot(string? status, int? errorCode, string? body)
    {
        if (!string.IsNullOrEmpty(status))
        {
            ProviderStatus = status;
        }

        ProviderErrorCode = errorCode;
        if (!ContentRedacted && body != null)
        {
            Body = body;
        }

        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkResendOf(int originalNotificationId, string idempotencyKey)
    {
        OriginalNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Body = null;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public bool DidNotReachShopper()
    {
        if (string.IsNullOrEmpty(ProviderMessageSid))
        {
            return true;
        }

        return ProviderStatus is "failed" or "undelivered" or "canceled" or "failed_to_submit";
    }

    public bool IsScheduledFollowUp()
    {
        return Kind == OrderNotificationKind.DeliveryFollowUp
            && !string.IsNullOrEmpty(ProviderMessageSid)
            && ProviderStatus is "scheduled" or "accepted" or "queued";
    }
}

public enum OrderNotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3
}
