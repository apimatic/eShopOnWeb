using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public enum NotificationKind
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCancelled,
    Resend
}

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderNotification() { }

    public OrderNotification(int orderId, int contactNumberId, string buyerId, NotificationKind kind,
        string body, DateTimeOffset createdAt, DateTimeOffset? scheduledFor = null, int? originalNotificationId = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
        ScheduledFor = scheduledFor;
        OriginalNotificationId = originalNotificationId;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public DateTimeOffset? CancellationRequestedAt { get; private set; }
    public int? CancellationErrorCode { get; private set; }

    public void RecordProviderState(string sid, string status, int? errorCode,
        DateTimeOffset? dateCreated, DateTimeOffset? dateSent, DateTimeOffset updatedAt)
    {
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderDateCreated = dateCreated;
        ProviderDateSent = dateSent;
        UpdatedAt = updatedAt;
    }

    public void RecordProviderFailure(int? errorCode, DateTimeOffset updatedAt)
    {
        ProviderStatus = "provider-request-failed";
        ProviderErrorCode = errorCode;
        UpdatedAt = updatedAt;
    }

    public void DisposeContent(DateTimeOffset disposedAt)
    {
        Body = null;
        ContentDisposedAt = disposedAt;
        UpdatedAt = disposedAt;
    }

    public void RecordCancellationRequested(DateTimeOffset requestedAt, int? errorCode = null)
    {
        CancellationRequestedAt = requestedAt;
        CancellationErrorCode = errorCode;
        UpdatedAt = requestedAt;
    }
}
