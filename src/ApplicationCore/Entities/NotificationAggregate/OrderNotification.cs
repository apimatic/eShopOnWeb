using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        int? contactNumberId,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? sendAt = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = body;
        SendAt = sendAt;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = sendAt.HasValue ? "scheduled" : "queued";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public bool ContentDisposed { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SendAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderAcceptance(string sid, string status, int? errorCode)
    {
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
    }

    public void RecordSendFailure(int? errorCode)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = errorCode;
    }

    public void ApplyProviderDeliveryState(string status, int? errorCode)
    {
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        if (ContentDisposed)
        {
            Body = null;
        }
    }

    public void ApplyProviderState(string status, int? errorCode, string? body)
    {
        ApplyProviderDeliveryState(status, errorCode);
        if (ContentDisposed)
        {
            return;
        }

        if (body == string.Empty)
        {
            MarkContentDisposed();
        }
    }

    public void MarkContentDisposed()
    {
        ContentDisposed = true;
        Body = null;
    }

    public bool IsScheduledPending()
    {
        if (string.IsNullOrEmpty(ProviderMessageSid))
        {
            return false;
        }

        var status = ProviderStatus?.ToLowerInvariant();
        return status is "scheduled" or "accepted";
    }
}
