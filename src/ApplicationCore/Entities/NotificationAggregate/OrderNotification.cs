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
        string destinationNumber,
        int? contactNumberId,
        NotificationKind kind,
        string body,
        DateTimeOffset? scheduledFor = null,
        int? resentFromNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        DestinationNumber = destinationNumber;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = body;
        ScheduledFor = scheduledFor;
        ResentFromNotificationId = resentFromNotificationId;
        DeliveryStatus = scheduledFor.HasValue ? "pending_schedule" : "pending_send";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string DestinationNumber { get; private set; }
    public int? ContactNumberId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string DeliveryStatus { get; private set; }
    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastProviderSyncAt { get; private set; }
    public int? ResentFromNotificationId { get; private set; }

    public void RecordProviderResult(
        string? providerMessageSid,
        string deliveryStatus,
        string? errorCode,
        string? errorMessage)
    {
        Guard.Against.NullOrEmpty(deliveryStatus, nameof(deliveryStatus));

        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = deliveryStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        LastProviderSyncAt = DateTimeOffset.UtcNow;
    }

    public void MarkSendFailed(string? errorCode, string? errorMessage)
    {
        DeliveryStatus = "send_failed";
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        LastProviderSyncAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
    }

    public static bool IsTerminalStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        return status.Equals("delivered", StringComparison.OrdinalIgnoreCase)
            || status.Equals("undelivered", StringComparison.OrdinalIgnoreCase)
            || status.Equals("failed", StringComparison.OrdinalIgnoreCase)
            || status.Equals("canceled", StringComparison.OrdinalIgnoreCase)
            || status.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
            || status.Equals("send_failed", StringComparison.OrdinalIgnoreCase);
    }
}
