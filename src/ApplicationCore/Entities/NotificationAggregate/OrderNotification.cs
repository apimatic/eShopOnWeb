using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string? destination,
        string? body,
        int? sourceNotificationId = null,
        DateTimeOffset? scheduledFor = null)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Destination = destination;
        Body = body;
        SourceNotificationId = sourceNotificationId;
        ScheduledFor = scheduledFor;
        DeliveryStatus = "pending";
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? ProviderSid { get; private set; }
    public string DeliveryStatus { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? Destination { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? DateSent { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }

    public void ApplyProviderState(
        string? providerSid,
        string status,
        string? body,
        string? destination,
        string? dateSent,
        int? errorCode,
        string? errorMessage)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        if (!string.IsNullOrWhiteSpace(providerSid))
        {
            ProviderSid = providerSid;
        }

        DeliveryStatus = status;

        if (!ContentRedacted && body is not null)
        {
            Body = body;
        }

        if (!string.IsNullOrWhiteSpace(destination))
        {
            Destination = destination;
        }

        DateSent = dateSent;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed(string errorMessage)
    {
        DeliveryStatus = "failed";
        ErrorMessage = errorMessage;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkRedacted()
    {
        Body = null;
        ContentRedacted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public string? BodyForDisplay() => ContentRedacted ? null : Body;
}
