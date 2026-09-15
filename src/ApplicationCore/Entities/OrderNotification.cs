using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, int? contactNumberId,
        string destination, NotificationKind kind, string body, DateTimeOffset createdAt,
        int? resendsNotificationId = null, string? idempotencyKey = null)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Destination = destination;
        Kind = kind;
        Body = body;
        CreatedAt = createdAt;
        ProviderStatus = "pending";
        ResendsNotificationId = resendsNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = string.Empty;
    public int? ContactNumberId { get; private set; }
    public string Destination { get; private set; } = string.Empty;
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = string.Empty;
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public int? ResendsNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderResult(string sid, string status, int? errorCode,
        string? errorMessage, DateTimeOffset? dateSent, DateTimeOffset? scheduledFor = null)
    {
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ProviderDateSent = dateSent;
        if (scheduledFor.HasValue)
        {
            ScheduledFor = scheduledFor;
        }
    }

    public void RecordSubmissionFailure(int? errorCode, string outcome)
    {
        ProviderStatus = "failed_to_submit";
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = outcome;
    }

    public void RecordCancellationFailure(int? errorCode, string outcome)
    {
        ProviderStatus = "cancellation_failed";
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = outcome;
    }

    public void MarkContentDisposed(DateTimeOffset now)
    {
        Body = null;
        ContentDisposedAt = now;
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
