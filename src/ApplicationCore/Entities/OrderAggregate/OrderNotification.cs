using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderNotification() { }
    #pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string destinationNumber,
        string body,
        DateTimeOffset? sendAt = null,
        int? parentNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.Null(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        Body = body;
        SendAt = sendAt;
        ParentNotificationId = parentNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SendAt { get; private set; }
    public DateTimeOffset? LastProviderSyncAt { get; private set; }
    public bool ContentDisposed { get; private set; }
    public int? ParentNotificationId { get; private set; }
    public string? LocalFailure { get; private set; }

    public void RecordAccepted(string providerMessageSid, string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderMessageSid = providerMessageSid;
        ProviderStatus = status;
        ErrorCode = errorCode;
        LocalFailure = null;
        LastProviderSyncAt = DateTimeOffset.UtcNow;
    }

    public void RecordLocalFailure(string reason)
    {
        Guard.Against.NullOrEmpty(reason, nameof(reason));
        LocalFailure = reason;
        ProviderStatus = "not_sent";
        LastProviderSyncAt = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderState(string status, int? errorCode, string? body)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ErrorCode = errorCode;
        LastProviderSyncAt = DateTimeOffset.UtcNow;

        if (ContentDisposed)
        {
            Body = null;
            return;
        }

        if (body != null)
        {
            Body = body;
        }
    }

    public void MarkContentDisposed()
    {
        ContentDisposed = true;
        Body = null;
    }

    public bool DidNotReachShopper()
    {
        if (!string.IsNullOrEmpty(LocalFailure) || string.IsNullOrEmpty(ProviderMessageSid))
        {
            return true;
        }

        return ProviderStatus is "failed" or "undelivered";
    }
}
