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
        OrderNotificationKind kind,
        string body,
        string? destinationNumber,
        DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationNumber = destinationNumber;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
        Status = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? Body { get; private set; }
    public string? DestinationNumber { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public void AttachProviderAccepted(string? sid, string? status)
    {
        ProviderSid = sid;
        Status = string.IsNullOrEmpty(status) ? "accepted" : status;
        ErrorCode = null;
        ErrorMessage = null;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed(string safeMessage)
    {
        Status = "send_failed";
        ErrorMessage = safeMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderSnapshot(string? status, int? errorCode, string? errorMessage, string? body)
    {
        if (!string.IsNullOrEmpty(status))
        {
            Status = status;
        }

        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;

        if (ContentRedacted)
        {
            Body = null;
            return;
        }

        if (string.IsNullOrEmpty(body))
        {
            Body = body;
            ContentRedacted = true;
            return;
        }

        Body = body;
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Body = null;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkCancelledAtProvider(string? status)
    {
        Status = string.IsNullOrEmpty(status) ? "canceled" : status;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }
}
