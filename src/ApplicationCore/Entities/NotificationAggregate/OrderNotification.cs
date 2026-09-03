using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string body,
        string toNumber,
        string fromNumber)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(fromNumber, nameof(fromNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        ToNumber = toNumber;
        FromNumber = fromNumber;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? ProviderSid { get; private set; }
    public string ProviderStatus { get; private set; } = "pending";
    public string Body { get; private set; }
    public string ToNumber { get; private set; }
    public string FromNumber { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderResult(string? sid, string status, int? errorCode, string? errorMessage, DateTimeOffset? scheduledSendAt)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderSid = sid;
        ProviderStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ScheduledSendAt = scheduledSendAt;
    }

    public void RecordSendFailure(string errorMessage)
    {
        ProviderStatus = "send_failed";
        ErrorMessage = errorMessage;
    }

    public void ApplyProviderStatus(string status, int? errorCode, string? errorMessage, string? body)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        if (!ContentRedacted && body is not null)
        {
            Body = body;
        }
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Body = string.Empty;
    }

    public void MarkAsResend(int originalNotificationId, string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(originalNotificationId, nameof(originalNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
        Kind = OrderNotificationKind.Resend;
    }

    public bool DidNotReachShopper()
    {
        return ProviderStatus is "failed" or "undelivered" or "send_failed" or "canceled";
    }

    public bool IsScheduledPending()
    {
        return Kind == OrderNotificationKind.DeliveryFollowUp
            && ProviderStatus is "scheduled" or "queued" or "accepted"
            && !string.IsNullOrEmpty(ProviderSid);
    }
}
