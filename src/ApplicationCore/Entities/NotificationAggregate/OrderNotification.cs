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
        OrderNotificationKind kind,
        string destinationNumber,
        string? body,
        string? providerMessageSid,
        string? providerStatus,
        int? providerErrorCode,
        DateTimeOffset? scheduledAt,
        DateTimeOffset? dateSent,
        int? originalNotificationId,
        string? resendIdempotencyKey)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        Body = body;
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
        ScheduledAt = scheduledAt;
        DateSent = dateSent;
        OriginalNotificationId = originalNotificationId;
        ResendIdempotencyKey = resendIdempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
        ContentRedacted = false;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public DateTimeOffset? DateSent { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? ResendIdempotencyKey { get; private set; }

    public void ApplyProviderState(string? sid, string? status, int? errorCode, string? body, DateTimeOffset? dateSent, bool contentRedacted)
    {
        if (!string.IsNullOrEmpty(sid))
        {
            ProviderMessageSid = sid;
        }

        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        DateSent = dateSent ?? DateSent;
        LastSyncedAt = DateTimeOffset.UtcNow;

        if (contentRedacted)
        {
            RedactContent();
        }
        else if (!ContentRedacted && body != null)
        {
            Body = body;
        }
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool IsTerminalStatus()
    {
        if (string.IsNullOrEmpty(ProviderStatus))
        {
            return false;
        }

        return ProviderStatus is "delivered" or "undelivered" or "failed" or "canceled" or "received" or "read";
    }

    public bool DidNotReachShopper()
    {
        return ProviderStatus is "failed" or "undelivered" || string.IsNullOrEmpty(ProviderMessageSid);
    }

    public bool IsCancellableScheduledMessage()
    {
        if (Kind != OrderNotificationKind.DeliveryFollowUp || string.IsNullOrEmpty(ProviderMessageSid))
        {
            return false;
        }

        return ProviderStatus is "scheduled" or "queued" or "accepted" or null or "";
    }
}
