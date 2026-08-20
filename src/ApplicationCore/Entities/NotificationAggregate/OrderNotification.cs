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
        int? contactNumberId,
        string destination,
        DateTimeOffset? scheduledSendAt = null,
        int? resentFromNotificationId = null,
        string? idempotencyKey = null,
        object? unusedEfConstructorGuard = null)
    {
        _ = unusedEfConstructorGuard;
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destination, nameof(destination));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        ContactNumberId = contactNumberId;
        Destination = destination;
        ScheduledSendAt = scheduledSendAt;
        ResentFromNotificationId = resentFromNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
        ContentRedacted = false;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string Destination { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? ResentFromNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void ApplyProviderResult(
        string? messageSid,
        string? status,
        int? errorCode,
        string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(messageSid))
        {
            ProviderMessageSid = messageSid;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            ProviderStatus = status;
        }

        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void MarkSendFailed(string? errorMessage = null)
    {
        ProviderStatus = "failed";
        ProviderErrorMessage = errorMessage;
    }

    public void RedactContent()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }

    public bool IsTerminalProviderStatus()
    {
        return ProviderStatus is "delivered" or "undelivered" or "failed" or "canceled" or "received" or "read";
    }

    public bool DidNotReachShopper()
    {
        return ProviderStatus is "failed" or "undelivered" or "canceled";
    }

    public bool IsCancellableFollowUp()
    {
        return Kind == OrderNotificationKind.DeliveryFollowUp
            && ProviderMessageSid is not null
            && ProviderStatus is "scheduled" or "queued" or "accepted" or "pending";
    }
}
