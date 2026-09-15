using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        int? contactNumberId,
        string destination,
        NotificationKind kind,
        string body,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Destination = destination;
        Kind = kind;
        Body = body;
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = "pending";
        StatusUpdatedAt = createdAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; } = null!;
    public int? ContactNumberId { get; private set; }
    public string Destination { get; private set; } = null!;
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = null!;
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset StatusUpdatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderState(ProviderMessageState state, DateTimeOffset updatedAt)
    {
        ProviderMessageSid = state.Sid;
        ProviderStatus = state.Status;
        ProviderErrorCode = state.ErrorCode;
        ProviderErrorMessage = state.ErrorMessage;
        StatusUpdatedAt = updatedAt;
    }

    public void RecordSubmissionFailure(DateTimeOffset updatedAt)
    {
        ProviderStatus = "submission_failed";
        ProviderErrorMessage = "The messaging provider did not accept the message.";
        StatusUpdatedAt = updatedAt;
    }

    public void DisposeContent(DateTimeOffset disposedAt)
    {
        Body = null;
        ContentDisposedAt = disposedAt;
    }
}

public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3,
    Resend = 4
}
