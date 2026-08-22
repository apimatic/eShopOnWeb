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
        NotificationKind kind,
        string? destination,
        string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Destination = destination;
        Body = body;
        CreatedUtc = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Destination { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? DateSent { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset CreatedUtc { get; private set; }

    public void ApplyProviderResult(
        string? providerSid,
        string? status,
        int? errorCode,
        string? errorMessage,
        string? dateSent)
    {
        ProviderSid = providerSid;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        DateSent = dateSent;
    }

    public void MarkScheduled(DateTimeOffset sendAt)
    {
        ScheduledSendAt = sendAt;
    }

    public void MarkResendOf(int sourceNotificationId, string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(sourceNotificationId, nameof(sourceNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public void MarkFollowUpOf(int sourceNotificationId)
    {
        Guard.Against.NegativeOrZero(sourceNotificationId, nameof(sourceNotificationId));
        SourceNotificationId = sourceNotificationId;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
