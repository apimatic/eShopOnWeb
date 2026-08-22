using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    public const string LocalSendFailedStatus = "send_failed";
    public const string SkippedNoDestinationStatus = "skipped_no_destination";

    #pragma warning disable CS8618
    private OrderNotification() { }
    #pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string body,
        int? contactNumberId,
        string? destinationPhoneNumber,
        DateTimeOffset? scheduledSendAt = null,
        int? resendOfNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        ContactNumberId = contactNumberId;
        DestinationPhoneNumber = destinationPhoneNumber;
        ScheduledSendAt = scheduledSendAt;
        ResendOfNotificationId = resendOfNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = string.IsNullOrEmpty(destinationPhoneNumber)
            ? SkippedNoDestinationStatus
            : "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string? DestinationPhoneNumber { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; } = "pending";
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? ResendOfNotificationId { get; private set; }

    public bool HasProviderMessage => !string.IsNullOrEmpty(ProviderMessageSid);

    public bool DidNotReachShopper()
    {
        if (string.Equals(ProviderStatus, SkippedNoDestinationStatus, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!HasProviderMessage)
        {
            return true;
        }

        return ProviderStatus is "failed" or "undelivered" or LocalSendFailedStatus;
    }

    public bool IsScheduledFollowUp()
    {
        return Kind == OrderNotificationKind.DeliveryFollowUp
            && string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase)
            && HasProviderMessage;
    }

    public void ApplyProviderResult(string sid, string status, int? errorCode, DateTimeOffset? dateSent)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderDateSent = dateSent;
    }

    public void MarkSendFailed()
    {
        ProviderStatus = LocalSendFailedStatus;
    }

    public void RefreshFromProvider(string status, int? errorCode, DateTimeOffset? dateSent, string? providerBody)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderDateSent = dateSent;

        if (ContentRedacted)
        {
            Body = null;
            return;
        }

        if (providerBody != null)
        {
            Body = providerBody;
        }
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Body = null;
    }

    public string BodyForResend()
    {
        return Kind switch
        {
            OrderNotificationKind.OrderPlaced => NotificationMessageText.OrderPlaced(OrderId),
            OrderNotificationKind.OrderDispatched => NotificationMessageText.OrderDispatched(OrderId),
            OrderNotificationKind.DeliveryFollowUp => NotificationMessageText.DeliveryFollowUp(OrderId),
            OrderNotificationKind.OrderCancelled => NotificationMessageText.OrderCancelled(OrderId),
            OrderNotificationKind.Resend when !string.IsNullOrEmpty(Body) => Body!,
            _ => Body ?? NotificationMessageText.OrderPlaced(OrderId)
        };
    }
}
