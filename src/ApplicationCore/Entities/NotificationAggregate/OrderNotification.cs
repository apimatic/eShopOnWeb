using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, int contactNumberId,
        NotificationKind kind, string body, DateTimeOffset? scheduledFor = null,
        int? originalNotificationId = null)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = body;
        ScheduledFor = scheduledFor;
        OriginalNotificationId = originalNotificationId;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = null!;
    public int ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = "pending";
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? LastProviderSyncAt { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public bool CancellationRequested { get; private set; }

    public void RecordProviderState(ProviderMessage message)
    {
        ProviderMessageSid = message.Sid;
        ProviderStatus = message.Status;
        ProviderErrorCode = message.ErrorCode;
        ProviderDateSent = message.DateSent;
        LastProviderSyncAt = DateTimeOffset.UtcNow;
        if (string.Equals(message.Status, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            CancellationRequested = false;
        }
    }

    public void RecordProviderFailure(int? errorCode = null)
    {
        ProviderStatus = "provider-error";
        ProviderErrorCode = errorCode;
        LastProviderSyncAt = DateTimeOffset.UtcNow;
    }

    public void RequestCancellation()
    {
        CancellationRequested = true;
    }

    public void DisposeContent()
    {
        Body = null;
        ContentDisposedAt = DateTimeOffset.UtcNow;
    }
}

public enum NotificationKind
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCancelled,
    Resend
}
