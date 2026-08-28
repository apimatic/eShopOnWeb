using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, int contactNumberId,
        NotificationKind kind, string body, DateTimeOffset createdAt, int? sourceNotificationId = null)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId);
        BuyerId = Guard.Against.NullOrWhiteSpace(buyerId);
        ContactNumberId = Guard.Against.NegativeOrZero(contactNumberId);
        Kind = kind;
        Body = Guard.Against.NullOrWhiteSpace(body);
        CreatedAt = createdAt;
        ProviderStatus = "pending";
        SourceNotificationId = sourceNotificationId;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? LastProviderSyncAt { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public int? SourceNotificationId { get; private set; }

    public void RecordProviderResult(string sid, string status, int? errorCode,
        DateTimeOffset? dateSent, DateTimeOffset syncedAt, DateTimeOffset? scheduledFor = null)
    {
        ProviderMessageSid = Guard.Against.NullOrWhiteSpace(sid);
        ProviderStatus = Guard.Against.NullOrWhiteSpace(status);
        ProviderErrorCode = errorCode;
        ProviderDateSent = dateSent;
        LastProviderSyncAt = syncedAt;
        ScheduledFor = scheduledFor;
    }

    public void RecordProviderFailure(int? errorCode, DateTimeOffset attemptedAt)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = errorCode;
        LastProviderSyncAt = attemptedAt;
    }

    public void RefreshProviderStatus(string status, int? errorCode,
        DateTimeOffset? dateSent, DateTimeOffset syncedAt)
    {
        ProviderStatus = Guard.Against.NullOrWhiteSpace(status);
        ProviderErrorCode = errorCode;
        ProviderDateSent = dateSent;
        LastProviderSyncAt = syncedAt;
    }

    public void DisposeContent(DateTimeOffset disposedAt)
    {
        Body = null;
        ContentDisposedAt = disposedAt;
    }
}
