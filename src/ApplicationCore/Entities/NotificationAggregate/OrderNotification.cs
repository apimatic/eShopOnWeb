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
        int contactNumberId,
        NotificationKind kind,
        string body,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? originalNotificationId = null,
        string? resendIdempotencyKey = null)
    {
        OrderId = orderId;
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = Guard.Against.NullOrEmpty(body, nameof(body));
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        OriginalNotificationId = originalNotificationId;
        ResendIdempotencyKey = resendIdempotencyKey;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public string? ProviderSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? LastCheckedAt { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public DateTimeOffset? CancellationRequestedAt { get; private set; }
    public int? CancellationErrorCode { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? ResendIdempotencyKey { get; private set; }

    public void RecordProviderState(string sid, string status, int? errorCode,
        DateTimeOffset? dateSent, DateTimeOffset checkedAt)
    {
        ProviderSid = Guard.Against.NullOrEmpty(sid, nameof(sid));
        ProviderStatus = Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderErrorCode = errorCode;
        ProviderDateSent = dateSent;
        LastCheckedAt = checkedAt;
    }

    public void RecordSendFailure(int? errorCode, DateTimeOffset checkedAt)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = errorCode;
        LastCheckedAt = checkedAt;
    }

    public void DisposeContent(DateTimeOffset disposedAt)
    {
        Body = null;
        ContentDisposedAt = disposedAt;
    }

    public void RecordCancellationFailure(int? errorCode, DateTimeOffset attemptedAt)
    {
        CancellationRequestedAt = attemptedAt;
        CancellationErrorCode = errorCode;
    }

    public void RecordCancellationSuccess(DateTimeOffset attemptedAt)
    {
        CancellationRequestedAt = attemptedAt;
        CancellationErrorCode = null;
    }

    public void DetachContactNumber() => ContactNumberId = null;
}

public enum NotificationKind
{
    OrderPlaced,
    OrderDispatched,
    DeliveryFollowUp,
    OrderCancelled,
    Resend
}
