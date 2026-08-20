using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string destination,
        string? body,
        int? parentNotificationId = null,
        string? idempotencyKey = null,
        DateTimeOffset? sendAt = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destination, nameof(destination));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Destination = destination;
        Body = body;
        ParentNotificationId = parentNotificationId;
        IdempotencyKey = idempotencyKey;
        SendAt = sendAt;
        Status = "pending";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string Destination { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderSid { get; private set; }
    public string Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? ProviderDateSent { get; private set; }
    public string? ProviderDateCreated { get; private set; }
    public DateTimeOffset? SendAt { get; private set; }
    public int? ParentNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public bool ContentDisposed { get; private set; }

    public void ApplyProviderState(
        string? providerSid,
        string? status,
        int? errorCode,
        string? errorMessage,
        string? providerDateSent,
        string? providerDateCreated,
        string? body)
    {
        if (!string.IsNullOrWhiteSpace(providerSid))
        {
            ProviderSid = providerSid;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            Status = status;
        }

        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        ProviderDateSent = providerDateSent;
        ProviderDateCreated = providerDateCreated;

        if (!ContentDisposed && body is not null)
        {
            Body = body;
        }
    }

    public void MarkSendFailed(string reason)
    {
        Status = "failed";
        ErrorMessage = reason;
    }

    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
    }
}
