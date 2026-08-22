using System;
using System.Diagnostics;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

[DebuggerDisplay("OrderNotification {Id} {Kind}")]
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
        string body,
        int? parentNotificationId = null)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        Body = body;
        ParentNotificationId = parentNotificationId;
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
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? DateSent { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public int? ParentNotificationId { get; private set; }

    public void MarkScheduledFor(DateTimeOffset sendAt) => ScheduledFor = sendAt;

    public void RecordAccepted(string? providerSid, string? providerStatus, string? dateSent)
    {
        ProviderSid = providerSid;
        ProviderStatus = providerStatus;
        DateSent = dateSent;
        ErrorCode = null;
        ErrorMessage = null;
    }

    public void RecordFailure(string? providerStatus, int? errorCode, string? errorMessage)
    {
        ProviderStatus = providerStatus ?? "failed";
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void ApplyProviderSnapshot(string? status, int? errorCode, string? errorMessage, string? dateSent, string? body)
    {
        if (!string.IsNullOrEmpty(status))
        {
            ProviderStatus = status;
        }

        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        DateSent = dateSent ?? DateSent;

        if (ContentRedacted)
        {
            Body = null;
            return;
        }

        if (body is not null)
        {
            Body = body;
        }
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool IsCancellableFollowUp()
    {
        if (Kind != NotificationKind.DeliveryFollowUp || string.IsNullOrEmpty(ProviderSid))
        {
            return false;
        }

        if (string.IsNullOrEmpty(ProviderStatus))
        {
            return true;
        }

        return ProviderStatus is "scheduled" or "queued" or "accepted" or "sending";
    }

    public override string ToString() => $"OrderNotification {Id}";
}
