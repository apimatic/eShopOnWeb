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
        int? contactNumberId,
        NotificationType type,
        string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        ForOrderId = orderId;
        BuyerId = buyerId;
        DestinationContactId = contactNumberId;
        Type = type;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int ForOrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? DestinationContactId { get; private set; }
    public NotificationType Type { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int? ResentFromNotificationId { get; private set; }
    public string? ResendIdempotencyKey { get; private set; }

    public void MarkScheduled(DateTimeOffset sendAt)
    {
        ScheduledFor = sendAt;
        ProviderStatus = "scheduled";
    }

    public void ApplyProviderResult(
        string? sid,
        string status,
        int? errorCode,
        string? errorMessage)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void MarkSendFailed(string reason)
    {
        ProviderStatus = "failed";
        ProviderErrorMessage = reason;
    }

    public void AttachResend(int sourceNotificationId, string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(sourceNotificationId, nameof(sourceNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        ResentFromNotificationId = sourceNotificationId;
        ResendIdempotencyKey = idempotencyKey;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
