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
        string? destinationNumber,
        string? body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

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
    public string? DestinationNumber { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public string? DateSent { get; private set; }
    public string? FailureReason { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public void ApplyProviderResult(
        string? providerSid,
        string? providerStatus,
        int? errorCode,
        string? errorMessage,
        string? dateSent,
        string? failureReason)
    {
        ProviderSid = providerSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        DateSent = dateSent;
        FailureReason = failureReason;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkScheduled(DateTimeOffset sendAt)
    {
        ScheduledSendAt = sendAt;
    }

    public void MarkResendOf(int originalNotificationId)
    {
        Guard.Against.NegativeOrZero(originalNotificationId, nameof(originalNotificationId));
        OriginalNotificationId = originalNotificationId;
        Kind = NotificationKind.Resend;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool IsTerminalStatus()
    {
        return ProviderStatus is "delivered" or "failed" or "undelivered" or "canceled" or "received";
    }

    public bool DidNotReachShopper()
    {
        if (string.IsNullOrEmpty(ProviderSid) && !string.IsNullOrEmpty(FailureReason))
        {
            return true;
        }

        return ProviderStatus is "failed" or "undelivered" or "canceled";
    }
}
