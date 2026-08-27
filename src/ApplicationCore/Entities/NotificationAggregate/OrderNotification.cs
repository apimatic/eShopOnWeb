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
        string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public bool SendAttempted { get; private set; }
    public bool SendFailed { get; private set; }
    public string? SendFailureReason { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public int? RelatedNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public bool BelongsTo(string buyerId) =>
        string.Equals(BuyerId, buyerId, StringComparison.Ordinal);

    public void MarkScheduled(DateTimeOffset sendAt)
    {
        ScheduledSendAt = sendAt;
    }

    public void RelateTo(int notificationId)
    {
        RelatedNotificationId = notificationId;
    }

    public void RecordProviderAccepted(string? sid, string? status, int? errorCode, string? errorMessage)
    {
        SendAttempted = true;
        SendFailed = false;
        SendFailureReason = null;
        ProviderSid = sid;
        ApplyProviderState(status, errorCode, errorMessage);
    }

    public void RecordSendFailure(string reason)
    {
        SendAttempted = true;
        SendFailed = true;
        SendFailureReason = reason;
    }

    public void ApplyProviderState(string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            ProviderStatus = status;
        }

        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void RedactLocalContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool ReachedShopper()
    {
        if (string.IsNullOrEmpty(ProviderStatus))
        {
            return false;
        }

        return ProviderStatus is "delivered" or "sent" or "read";
    }

    public bool CanResend()
    {
        if (ContentRedacted || string.IsNullOrEmpty(Body))
        {
            return false;
        }

        if (ReachedShopper())
        {
            return false;
        }

        return SendFailed
            || ProviderStatus is "failed" or "undelivered" or "canceled"
            || (SendAttempted && string.IsNullOrEmpty(ProviderSid));
    }
}
