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
        string destinationPhoneNumber,
        string? body,
        string? providerMessageSid,
        string? providerStatus,
        int? providerErrorCode,
        string? providerErrorMessage,
        DateTimeOffset? scheduledSendAt,
        int? resentFromNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationPhoneNumber, nameof(destinationPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationPhoneNumber = destinationPhoneNumber;
        Body = body;
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
        ScheduledSendAt = scheduledSendAt;
        ResentFromNotificationId = resentFromNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string DestinationPhoneNumber { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public int? ResentFromNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSyncedAt { get; private set; }

    public void ApplyProviderOutcome(string? status, int? errorCode, string? errorMessage, string? bodyFromProvider)
    {
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;

        if (ContentRedacted)
        {
            Body = null;
            return;
        }

        if (bodyFromProvider == string.Empty)
        {
            RedactContent();
            return;
        }

        if (bodyFromProvider != null)
        {
            Body = bodyFromProvider;
        }
    }

    public void MarkSendFailed(string errorMessage)
    {
        ProviderStatus = "failed";
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public bool IsCancellableFollowUp()
    {
        if (Kind != NotificationKind.DeliveryFollowUp)
        {
            return false;
        }

        if (string.IsNullOrEmpty(ProviderMessageSid))
        {
            return false;
        }

        var status = ProviderStatus?.ToLowerInvariant();
        return status is "scheduled" or "queued" or "accepted" or "sending";
    }
}
