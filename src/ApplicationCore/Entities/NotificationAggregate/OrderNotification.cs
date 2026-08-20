using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string destinationNumber,
        string body,
        int? sourceNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.Null(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        Body = body;
        SourceNotificationId = sourceNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? SendFailure { get; private set; }

    public bool IsResend => SourceNotificationId.HasValue;

    public void RecordAccepted(string providerSid, string providerStatus, DateTimeOffset? scheduledSendAt)
    {
        Guard.Against.NullOrEmpty(providerSid, nameof(providerSid));
        ProviderSid = providerSid;
        ProviderStatus = providerStatus;
        ScheduledSendAt = scheduledSendAt;
        LastSyncedAt = DateTimeOffset.UtcNow;
        SendFailure = null;
    }

    public void RecordSendFailure(string reason)
    {
        ProviderStatus = "failed";
        SendFailure = reason;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void SyncFromProvider(string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }

    public static bool IsTerminalProviderStatus(string? status)
    {
        return status is "delivered" or "undelivered" or "failed" or "canceled";
    }

    public bool DidNotReachShopper()
    {
        return ProviderStatus is "failed" or "undelivered" or "canceled";
    }
}
