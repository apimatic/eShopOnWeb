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
        string kind,
        string body,
        string destinationNumber,
        string? providerMessageSid,
        string providerStatus,
        int? errorCode,
        DateTimeOffset? scheduledAt,
        int? resentFromNotificationId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(kind, nameof(kind));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.NullOrEmpty(providerStatus, nameof(providerStatus));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body ?? string.Empty;
        DestinationNumber = destinationNumber;
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ErrorCode = errorCode;
        ScheduledAt = scheduledAt;
        ResentFromNotificationId = resentFromNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Kind { get; private set; }
    public string Body { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public int? ResentFromNotificationId { get; private set; }
    public DateTimeOffset? ContentDisposedAt { get; private set; }
    public bool ContentDisposed => ContentDisposedAt.HasValue;

    public void ApplyProviderState(string status, int? errorCode, string? bodyFromProvider)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ErrorCode = errorCode;
        if (ContentDisposed)
        {
            Body = string.Empty;
            return;
        }

        if (bodyFromProvider == null)
        {
            return;
        }

        if (bodyFromProvider.Length == 0 && !string.IsNullOrEmpty(Body))
        {
            MarkContentDisposed();
            return;
        }

        Body = bodyFromProvider;
    }

    public void MarkContentDisposed()
    {
        ContentDisposedAt = DateTimeOffset.UtcNow;
        Body = string.Empty;
    }
}
