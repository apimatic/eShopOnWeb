using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.Notifications;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string body,
        string destinationCanonicalNumber,
        int? contactNumberId)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destinationCanonicalNumber, nameof(destinationCanonicalNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationCanonicalNumber = destinationCanonicalNumber;
        ContactNumberId = contactNumberId;
        CreatedAt = DateTimeOffset.UtcNow;
        LastSyncedAt = CreatedAt;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string DestinationCanonicalNumber { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public int? ResentFromNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastSyncedAt { get; private set; }

    public void MarkScheduled(DateTimeOffset sendAt)
    {
        ScheduledSendAt = sendAt;
    }

    public void AttachResendMetadata(int originalNotificationId, string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(originalNotificationId, nameof(originalNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        ResentFromNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
        Kind = OrderNotificationKind.Resend;
    }

    public void RecordProviderAcceptance(string sid, string? status)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = null;
        ProviderErrorMessage = null;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void RecordProviderFailure(int? errorCode, string? errorMessage)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void SyncFromProvider(string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            ProviderStatus = status;
        }

        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public bool IsTerminalStatus()
    {
        return ProviderStatus is "delivered" or "undelivered" or "failed" or "canceled" or "received" or "read";
    }

    public bool DidNotReachShopper()
    {
        return ProviderStatus is "failed" or "undelivered" or "canceled"
            || (string.IsNullOrEmpty(ProviderMessageSid) && ProviderStatus == "failed");
    }
}
