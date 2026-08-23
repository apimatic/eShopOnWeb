using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string destinationNumber,
        string body)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        Body = body;
        ProviderStatus = "pending";
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? Body { get; private set; }
    public bool BodyRedacted { get; private set; }
    public string? ProviderSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? SendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public int? ResentFromNotificationId { get; private set; }

    public void AttachProviderResult(string? sid, string status, int? errorCode, string? errorMessage, DateTimeOffset? sendAt)
    {
        ProviderSid = sid;
        ProviderStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        if (sendAt.HasValue)
        {
            SendAt = sendAt;
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RefreshFromProvider(string status, int? errorCode, string? errorMessage, string? body)
    {
        ProviderStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        if (!BodyRedacted && body != null)
        {
            Body = body;
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed(string errorMessage)
    {
        ProviderStatus = "failed";
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkRedacted(string? remainingBody)
    {
        BodyRedacted = true;
        Body = remainingBody;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAsResend(int sourceNotificationId, string idempotencyKey)
    {
        Kind = OrderNotificationKind.Resend;
        ResentFromNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
    }
}
