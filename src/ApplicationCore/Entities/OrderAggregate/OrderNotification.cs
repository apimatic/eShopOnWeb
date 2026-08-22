using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderNotification() {}
    #pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string body,
        string destinationPhoneNumber,
        int? contactNumberId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destinationPhoneNumber, nameof(destinationPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationPhoneNumber = destinationPhoneNumber;
        ContactNumberId = contactNumberId;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string DestinationPhoneNumber { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = "pending";
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastProviderSyncAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public int? SourceNotificationId { get; private set; }

    public void MarkScheduled(DateTimeOffset sendAt)
    {
        ScheduledSendAt = sendAt;
    }

    public void AttachProviderResult(string? sid, string status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(sid))
        {
            ProviderMessageSid = sid;
        }

        ProviderStatus = string.IsNullOrEmpty(status) ? ProviderStatus : status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastProviderSyncAt = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed(string reason)
    {
        ProviderStatus = "failed";
        ProviderErrorMessage = reason;
        LastProviderSyncAt = DateTimeOffset.UtcNow;
    }

    public void ApplyAsResend(int sourceNotificationId, string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(sourceNotificationId, nameof(sourceNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        Kind = OrderNotificationKind.Resend;
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public void RedactContent()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }

    public bool DidNotReachShopper()
    {
        var status = ProviderStatus?.ToLowerInvariant();
        return status is "failed" or "undelivered" or "canceled" or "cancelled";
    }
}
