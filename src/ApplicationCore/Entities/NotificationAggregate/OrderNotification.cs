using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        int contactNumberId,
        string buyerId,
        NotificationType type,
        string body,
        DateTimeOffset createdAt,
        DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = orderId;
        ContactNumberId = contactNumberId;
        BuyerId = buyerId;
        Type = type;
        Body = body;
        CreatedAt = createdAt;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public int ContactNumberId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationType Type { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProviderCreatedAt { get; private set; }
    public DateTimeOffset? ProviderSentAt { get; private set; }
    public DateTimeOffset? ProviderUpdatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ContentRedactedAt { get; private set; }
    public DateTimeOffset? CancellationRequestedAt { get; private set; }
    public DateTimeOffset? CancellationCompletedAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderState(ProviderMessage message, DateTimeOffset observedAt)
    {
        ProviderMessageSid = message.Sid;
        ProviderStatus = message.Status;
        ProviderErrorCode = message.ErrorCode;
        ProviderCreatedAt = message.DateCreated ?? ProviderCreatedAt;
        ProviderSentAt = message.DateSent ?? ProviderSentAt;
        ProviderUpdatedAt = observedAt;
        if (string.Equals(message.Status, "canceled", StringComparison.OrdinalIgnoreCase))
        {
            CancellationCompletedAt ??= observedAt;
        }
    }

    public void RecordProviderFailure(int? errorCode, DateTimeOffset observedAt)
    {
        ProviderStatus = "provider-error";
        ProviderErrorCode = errorCode;
        ProviderUpdatedAt = observedAt;
    }

    public void RedactContent(DateTimeOffset redactedAt)
    {
        Body = null;
        ContentRedactedAt ??= redactedAt;
    }

    public void RequestCancellation(DateTimeOffset requestedAt)
    {
        CancellationRequestedAt ??= requestedAt;
    }

    public void CompleteCancellationAttempt(DateTimeOffset completedAt)
    {
        CancellationCompletedAt ??= completedAt;
    }
}
