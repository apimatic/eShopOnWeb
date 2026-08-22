using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    public const string LocalFailureStatus = "send_failed";
    public const string SkippedNoDestinationStatus = "skipped_no_destination";

#pragma warning disable CS8618
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string destinationPhoneNumber,
        string body,
        DateTimeOffset? scheduledFor = null,
        int? parentNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationPhoneNumber, nameof(destinationPhoneNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationPhoneNumber = destinationPhoneNumber;
        Body = body;
        ScheduledFor = scheduledFor;
        ParentNotificationId = parentNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string DestinationPhoneNumber { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ErrorCode { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public int? ParentNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool IsScheduledFollowUp =>
        Kind == OrderNotificationKind.DeliveryFeedback
        && !string.Equals(ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(ProviderStatus, "sent", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(ProviderStatus, "delivered", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(ProviderStatus, "undelivered", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(ProviderStatus, "failed", StringComparison.OrdinalIgnoreCase);

    public void ApplyProviderAcceptance(string sid, string status, int? errorCode, DateTimeOffset? dateCreated, DateTimeOffset? dateSent)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        ProviderSid = sid;
        ProviderStatus = status;
        ErrorCode = errorCode;
        ProviderDateCreated = dateCreated;
        ProviderDateSent = dateSent;
    }

    public void ApplyProviderSnapshot(string status, int? errorCode, string? body, DateTimeOffset? dateCreated, DateTimeOffset? dateSent)
    {
        ProviderStatus = status;
        ErrorCode = errorCode;
        ProviderDateCreated = dateCreated;
        ProviderDateSent = dateSent;
        if (!ContentRedacted)
        {
            Body = body;
        }
    }

    public void MarkSendFailed(int? errorCode)
    {
        ProviderStatus = LocalFailureStatus;
        ErrorCode = errorCode;
    }

    public void MarkRedacted()
    {
        ContentRedacted = true;
        Body = null;
    }
}
