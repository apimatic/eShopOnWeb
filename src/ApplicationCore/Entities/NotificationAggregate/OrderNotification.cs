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
        OrderNotificationKind kind,
        string destinationNumber,
        string body,
        string? idempotencyKey = null,
        int? resentFromNotificationId = null,
        DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.Null(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        Body = body;
        IdempotencyKey = idempotencyKey;
        ResentFromNotificationId = resentFromNotificationId;
        ScheduledSendAt = scheduledSendAt;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string DestinationNumber { get; private set; }
    public string Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? LocalFailure { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public int? ResentFromNotificationId { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public void RecordProviderAcceptance(string messageSid, string status)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        ProviderMessageSid = messageSid;
        ApplyProviderState(status, errorCode: null);
    }

    public void RecordLocalFailure(string reason)
    {
        Guard.Against.NullOrEmpty(reason, nameof(reason));
        LocalFailure = reason;
        ProviderStatus = "send_failed";
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderState(string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentRedacted()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }

    public bool DidNotReachShopper()
    {
        return ProviderStatus is "failed" or "undelivered" or "canceled" or "send_failed";
    }
}
