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
        int? contactNumberId,
        string destinationNumber,
        OrderNotificationType type,
        string body,
        DateTimeOffset? scheduledAt = null,
        int? originalNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        DestinationNumber = destinationNumber;
        Type = type;
        Body = body;
        ScheduledAt = scheduledAt;
        OriginalNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string DestinationNumber { get; private set; }
    public OrderNotificationType Type { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? DateSent { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderAccepted(string messageSid, string status, DateTimeOffset? dateSent, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderMessageSid = messageSid;
        ApplyProviderState(status, dateSent, errorCode, errorMessage);
    }

    public void RecordSendFailure(string status, int? errorCode, string? errorMessage)
    {
        ProviderStatus = string.IsNullOrWhiteSpace(status) ? "failed" : status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderState(string status, DateTimeOffset? dateSent, int? errorCode, string? errorMessage)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderStatus = status;
        if (dateSent.HasValue)
        {
            DateSent = dateSent;
        }

        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentRedacted()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }

    public bool HasReachedShopper()
    {
        return string.Equals(ProviderStatus, "delivered", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, "read", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsInFlight()
    {
        return ProviderStatus is "pending" or "queued" or "sending" or "accepted" or "scheduled";
    }

    public bool CanResend()
    {
        if (ContentRedacted || string.IsNullOrEmpty(Body))
        {
            return false;
        }

        if (HasReachedShopper() || IsInFlight())
        {
            return false;
        }

        return true;
    }

    public bool IsScheduledFollowUpOutstanding()
    {
        return Type == OrderNotificationType.DeliveryFollowUp
            && !string.IsNullOrEmpty(ProviderMessageSid)
            && string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase);
    }
}
