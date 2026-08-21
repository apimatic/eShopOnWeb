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
        NotificationKind kind,
        string destinationNumber,
        string body,
        string? providerMessageSid,
        string providerStatus,
        int? providerErrorCode,
        DateTimeOffset? scheduledSendAt,
        int? sourceNotificationId)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.Null(body, nameof(body));
        Guard.Against.NullOrEmpty(providerStatus, nameof(providerStatus));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        Body = body;
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
        ScheduledSendAt = scheduledSendAt;
        SourceNotificationId = sourceNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string DestinationNumber { get; private set; }
    public string Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void ApplyProviderState(string status, int? errorCode, string? providerMessageSid)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        if (!string.IsNullOrEmpty(providerMessageSid))
        {
            ProviderMessageSid = providerMessageSid;
        }
    }

    public void MarkSendFailed(string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
    }

    public void RedactContent()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }

    public bool IsPendingFollowUp()
    {
        return Kind == NotificationKind.DeliveryFollowUp
            && !string.IsNullOrEmpty(ProviderMessageSid)
            && (string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ProviderStatus, "accepted", StringComparison.OrdinalIgnoreCase)
                || string.Equals(ProviderStatus, "queued", StringComparison.OrdinalIgnoreCase));
    }

    public bool DidNotReachShopper()
    {
        return string.Equals(ProviderStatus, "failed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "undelivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "canceled", StringComparison.OrdinalIgnoreCase);
    }
}
