using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string body,
        string? destination,
        DateTimeOffset? scheduledAt = null,
        int? originalNotificationId = null,
        string? resendIdempotencyKey = null)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        Destination = destination;
        ScheduledAt = scheduledAt;
        OriginalNotificationId = originalNotificationId;
        ResendIdempotencyKey = resendIdempotencyKey;
        DeliveryStatus = "pending";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? ProviderSid { get; private set; }
    public string DeliveryStatus { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? Body { get; private set; }
    public bool ContentDisposed { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public string? ProviderDateSent { get; private set; }
    public string? Destination { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? ResendIdempotencyKey { get; private set; }

    public void RecordAccepted(string? sid, string status, int? errorCode, string? errorMessage, string? dateSent)
    {
        ProviderSid = sid;
        DeliveryStatus = string.IsNullOrWhiteSpace(status) ? "queued" : status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ProviderDateSent = dateSent;
    }

    public void RecordSendFailure(string status, string? errorMessage)
    {
        DeliveryStatus = status;
        ErrorMessage = errorMessage;
    }

    public void ApplyProviderState(string status, int? errorCode, string? errorMessage, string? dateSent, string? body)
    {
        DeliveryStatus = string.IsNullOrWhiteSpace(status) ? DeliveryStatus : status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ProviderDateSent = dateSent ?? ProviderDateSent;
        if (ContentDisposed)
        {
            Body = null;
            return;
        }

        if (string.IsNullOrEmpty(body))
        {
            Body = body;
            if (body is not null)
            {
                ContentDisposed = true;
            }
        }
        else
        {
            Body = body;
        }
    }

    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }
}
