using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string body,
        string? providerMessageSid,
        string status,
        string? errorCode,
        string? errorMessage,
        DateTimeOffset? dateSent,
        DateTimeOffset? scheduledSendAt,
        int? sourceNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(status, nameof(status));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        DateSent = dateSent;
        ScheduledSendAt = scheduledSendAt;
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string Status { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DateSent { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void ApplyProviderState(string status, string? errorCode, string? errorMessage, DateTimeOffset? dateSent, string? body)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        DateSent = dateSent;

        if (ContentRedacted || string.IsNullOrEmpty(body))
        {
            MarkContentRedacted();
            return;
        }

        Body = body;
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Body = null;
    }
}
