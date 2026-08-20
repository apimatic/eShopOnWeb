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
        string destinationNumber,
        int? contactNumberId,
        DateTimeOffset? scheduledFor = null,
        int? parentNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(kind, nameof(kind));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationNumber = destinationNumber;
        ContactNumberId = contactNumberId;
        ScheduledFor = scheduledFor;
        ParentNotificationId = parentNotificationId;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Kind { get; private set; }
    public string? Body { get; private set; }
    public string DestinationNumber { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public bool ContentDisposed { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public int? ParentNotificationId { get; private set; }

    public void RecordProviderAccepted(string sid, string status)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = null;
        ProviderErrorMessage = null;
    }

    public void RecordProviderFailure(string? status, int? errorCode, string? errorMessage)
    {
        ProviderStatus = string.IsNullOrWhiteSpace(status) ? "failed" : status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void ApplyProviderSnapshot(string status, int? errorCode, string? errorMessage, string? body)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (ContentDisposed)
        {
            Body = null;
            return;
        }

        if (body is not null)
        {
            Body = body;
        }
    }

    public void MarkContentDisposed()
    {
        ContentDisposed = true;
        Body = null;
    }

    public bool IsFollowUpOutstanding()
    {
        if (Kind != NotificationKind.DeliveryFollowUp)
        {
            return false;
        }

        if (string.IsNullOrEmpty(ProviderMessageSid))
        {
            return false;
        }

        return ProviderStatus is "scheduled" or "queued" or "accepted" or "pending";
    }

    public bool DidNotReachShopper()
    {
        return ProviderStatus is "failed" or "undelivered";
    }
}
