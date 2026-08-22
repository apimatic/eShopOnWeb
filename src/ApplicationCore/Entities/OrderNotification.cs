using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string destinationPhoneNumber,
        string body,
        string? providerMessageSid,
        string providerStatus,
        int? providerErrorCode,
        DateTimeOffset? scheduledFor,
        int? originalNotificationId = null)
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
        ScheduledFor = scheduledFor;
        OriginalNotificationId = originalNotificationId;
        ContentRedacted = false;
        CreatedAt = DateTimeOffset.UtcNow;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string DestinationPhoneNumber { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSyncedAt { get; private set; }

    public void ApplyProviderState(string status, int? errorCode, string? bodyIfPresent)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        LastSyncedAt = DateTimeOffset.UtcNow;

        if (ContentRedacted)
        {
            Body = null;
            return;
        }

        if (bodyIfPresent != null)
        {
            Body = bodyIfPresent;
        }
    }

    public void MarkSendFailed(string status, int? errorCode)
    {
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public bool HasReachedShopper()
    {
        return string.Equals(ProviderStatus, "delivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "read", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsTerminalStatus()
    {
        return ProviderStatus is "delivered" or "undelivered" or "failed" or "canceled" or "read";
    }

    public bool IsScheduledFollowUpStillPending()
    {
        return Kind == OrderNotificationKind.DeliveryFollowUp
            && !string.IsNullOrEmpty(ProviderMessageSid)
            && ProviderStatus is "scheduled" or "accepted" or "queued";
    }
}
