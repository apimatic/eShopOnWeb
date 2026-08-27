using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(int orderId, int contactNumberId, NotificationKind kind, string body,
        DateTimeOffset createdAt, int? sourceNotificationId = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = body;
        CreatedAt = createdAt;
        SourceNotificationId = sourceNotificationId;
        ProviderStatus = "Pending";
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = "Pending";
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ProviderCreatedAt { get; private set; }
    public DateTimeOffset? ProviderSentAt { get; private set; }
    public DateTimeOffset? ProviderUpdatedAt { get; private set; }
    public DateTimeOffset? LastRefreshedAt { get; private set; }
    public DateTimeOffset? LastRefreshFailedAt { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public bool CancellationPending { get; private set; }

    public void RecordProviderResult(string? sid, string status, int? errorCode,
        DateTimeOffset? providerCreatedAt, DateTimeOffset? providerSentAt,
        DateTimeOffset? providerUpdatedAt, DateTimeOffset observedAt, DateTimeOffset? scheduledFor = null)
    {
        ProviderMessageSid = sid ?? ProviderMessageSid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderCreatedAt = providerCreatedAt ?? ProviderCreatedAt;
        ProviderSentAt = providerSentAt ?? ProviderSentAt;
        ProviderUpdatedAt = providerUpdatedAt ?? ProviderUpdatedAt;
        ScheduledFor = scheduledFor ?? ScheduledFor;
        LastRefreshedAt = observedAt;
        LastRefreshFailedAt = null;
        if (string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            CancellationPending = false;
        }
    }

    public void RecordProviderFailure(string status, DateTimeOffset observedAt)
    {
        ProviderStatus = status;
        LastRefreshFailedAt = observedAt;
    }

    public void MarkRefreshFailed(DateTimeOffset observedAt) => LastRefreshFailedAt = observedAt;
    public void RequestCancellation() => CancellationPending = true;
    public void DisposeContent(DateTimeOffset disposedAt)
    {
        Body = null;
        ContentDisposedAt = disposedAt;
    }
}

public enum NotificationKind
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCancelled
}
