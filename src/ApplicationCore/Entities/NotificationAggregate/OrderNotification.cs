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
        string kind,
        string body,
        string destinationCanonical)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(kind, nameof(kind));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destinationCanonical, nameof(destinationCanonical));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationCanonical = destinationCanonical;
        CreatedAt = DateTimeOffset.UtcNow;
        Status = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Kind { get; private set; }
    public string? Body { get; set; }
    public bool ContentRedacted { get; set; }
    public string DestinationCanonical { get; private set; }
    public string? ProviderSid { get; private set; }
    public string Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public int? ParentNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderAcceptance(string? providerSid, string? status, int? errorCode, string? errorMessage)
    {
        ProviderSid = providerSid;
        Status = string.IsNullOrWhiteSpace(status) ? "accepted" : status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void RecordSendFailure(string errorMessage)
    {
        Status = "failed";
        ErrorMessage = errorMessage;
    }

    public void ApplyProviderSnapshot(string? status, int? errorCode, string? errorMessage, string? body)
    {
        if (!string.IsNullOrWhiteSpace(status))
            Status = status;

        ErrorCode = errorCode;
        ErrorMessage = errorMessage;

        if (ContentRedacted)
            return;

        if (body is not null)
            Body = body;
    }

    public void MarkContentRedacted()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }

    public void SetSchedule(DateTimeOffset sendAtUtc)
    {
        ScheduledSendAt = sendAtUtc;
    }

    public void SetResendMetadata(int parentNotificationId, string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(parentNotificationId, nameof(parentNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        ParentNotificationId = parentNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public bool IsTerminalStatus()
    {
        return Status is "delivered" or "undelivered" or "failed" or "canceled";
    }

    public bool CanBeCancelledAtProvider()
    {
        return !string.IsNullOrWhiteSpace(ProviderSid)
               && Status is "scheduled" or "queued" or "accepted" or "pending";
    }

    public bool DidNotReachShopper()
    {
        return Status is not "delivered";
    }
}
