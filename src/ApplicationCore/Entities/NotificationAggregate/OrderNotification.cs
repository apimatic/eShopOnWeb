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
        string? destinationNumber = null,
        int? contactNumberId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.Null(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationNumber = destinationNumber;
        ContactNumberId = contactNumberId;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string? DestinationNumber { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = "pending";
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int? ResentFromNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void MarkScheduled(DateTimeOffset sendAt)
    {
        ScheduledSendAt = sendAt;
        ProviderStatus = "scheduled";
    }

    public void MarkNotSent(string reason)
    {
        ProviderStatus = "not_sent";
        ProviderErrorMessage = reason;
    }

    public void ApplyProviderResult(
        string? sid,
        string? status,
        int? errorCode,
        string? errorMessage,
        string? body = null)
    {
        if (!string.IsNullOrWhiteSpace(sid))
        {
            ProviderMessageSid = sid;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            ProviderStatus = status;
        }

        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;

        if (!ContentRedacted && body != null)
        {
            Body = body;
        }
    }

    public void AssignResend(int originalNotificationId, string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(originalNotificationId, nameof(originalNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        ResentFromNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
        Kind = OrderNotificationKind.Resend;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool DidNotReachShopper()
    {
        var status = ProviderStatus?.ToLowerInvariant();
        return status is "failed" or "undelivered" or "canceled" or "not_sent";
    }
}
