using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

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
        string destinationNumber,
        int? contactNumberId,
        DateTimeOffset? scheduledForUtc = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationNumber = destinationNumber;
        ContactNumberId = contactNumberId;
        ScheduledForUtc = scheduledForUtc;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedUtc = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public bool ContentDisposed { get; private set; }
    public string DestinationNumber { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedUtc { get; private set; }
    public DateTimeOffset? ScheduledForUtc { get; private set; }
    public DateTimeOffset? LastSyncedUtc { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public bool SendAccepted { get; private set; }

    public void RecordProviderAcceptance(string? providerSid, string? status, int? errorCode, string? errorMessage)
    {
        ProviderSid = providerSid;
        ProviderStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        SendAccepted = !string.IsNullOrEmpty(providerSid);
        LastSyncedUtc = DateTimeOffset.UtcNow;
    }

    public void RecordSendFailure(string errorMessage)
    {
        SendAccepted = false;
        ErrorMessage = errorMessage;
        ProviderStatus = "send_failed";
        LastSyncedUtc = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderSnapshot(string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
        {
            ProviderStatus = status;
        }
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        LastSyncedUtc = DateTimeOffset.UtcNow;
    }

    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
    }

    public bool CanResend()
    {
        if (ContentDisposed || string.IsNullOrEmpty(Body))
        {
            return false;
        }

        if (!SendAccepted || string.IsNullOrEmpty(ProviderSid))
        {
            return true;
        }

        return IsUnreachedStatus(ProviderStatus);
    }

    public bool IsCancellableSchedule()
    {
        if (Kind != OrderNotificationKind.DeliveryFollowUp || string.IsNullOrEmpty(ProviderSid))
        {
            return false;
        }

        var status = ProviderStatus?.ToLowerInvariant();
        return status is null or "scheduled" or "queued" or "accepted";
    }

    public static bool IsUnreachedStatus(string? status)
    {
        var value = status?.ToLowerInvariant();
        return value is "failed" or "undelivered" or "canceled" or "send_failed";
    }
}
