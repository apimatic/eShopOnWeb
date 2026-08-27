using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string body,
        string destinationPhoneNumber,
        int? parentNotificationId = null,
        DateTimeOffset? scheduledAt = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destinationPhoneNumber, nameof(destinationPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationPhoneNumber = destinationPhoneNumber;
        ParentNotificationId = parentNotificationId;
        ScheduledAt = scheduledAt;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = scheduledAt.HasValue ? "scheduled" : "queued";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public string? Body { get; private set; }
    public bool ContentDisposed { get; private set; }
    public int? ErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public int? ParentNotificationId { get; private set; }
    public string DestinationPhoneNumber { get; private set; }

    public void RecordProviderAcceptance(string providerMessageSid, string status)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderMessageSid = providerMessageSid;
        ProviderStatus = status;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void RecordSendFailure(int? errorCode, string status = "failed")
    {
        ErrorCode = errorCode;
        ProviderStatus = string.IsNullOrEmpty(status) ? "failed" : status;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderState(string status, int? errorCode, string? body)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderStatus = status;
        ErrorCode = errorCode;
        LastSyncedAt = DateTimeOffset.UtcNow;

        if (ContentDisposed)
        {
            Body = null;
            return;
        }

        if (body != null)
        {
            Body = body;
        }
    }

    public void MarkContentDisposed()
    {
        ContentDisposed = true;
        Body = null;
    }

    public bool DidNotReachShopper()
    {
        var status = ProviderStatus?.ToLowerInvariant();
        return status is "failed" or "undelivered" or "canceled";
    }
}
