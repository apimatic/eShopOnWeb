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
        string kind,
        string? destination,
        string? body,
        DateTimeOffset? scheduledFor = null,
        int? sourceNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(kind, nameof(kind));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Destination = destination;
        Body = body;
        ScheduledFor = scheduledFor;
        SourceNotificationId = sourceNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
        Status = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Kind { get; private set; }
    public string? Destination { get; private set; }
    public string? Body { get; private set; }
    public bool ContentDisposed { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? Direction { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public int? SourceNotificationId { get; private set; }

    public void ApplyProviderResult(string? sid, string? status, int? errorCode, string? errorMessage, string? direction, string? body = null)
    {
        ProviderSid = sid;
        Status = status ?? "failed";
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        if (direction is not null)
        {
            Direction = direction;
        }

        if (body is not null && !ContentDisposed)
        {
            Body = body;
        }
    }

    public void MarkSendFailed(string errorMessage)
    {
        Status = "failed";
        ErrorMessage = errorMessage;
    }

    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }
}
