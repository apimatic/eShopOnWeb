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
        string body,
        string? destinationPhoneNumber)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationPhoneNumber = destinationPhoneNumber;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string Body { get; private set; }
    public bool BodyRedacted { get; private set; }
    public string? DestinationPhoneNumber { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public string? ProviderErrorCode { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public int? OriginalNotificationId { get; private set; }

    public void RecordScheduledSendAt(DateTimeOffset sendAt)
    {
        ScheduledSendAt = sendAt;
    }

    public void RecordOriginalNotification(int originalNotificationId)
    {
        OriginalNotificationId = originalNotificationId;
    }

    public void RecordProviderAcceptance(string providerMessageSid, string status)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = status;
    }

    public void RecordSendFailure(string? errorCode)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = errorCode;
    }

    public void SyncProviderState(string status, string? body, string? errorCode, DateTimeOffset? dateSent)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderDateSent = dateSent;

        if (BodyRedacted)
        {
            Body = string.Empty;
            return;
        }

        if (body != null)
        {
            Body = body;
            if (body.Length == 0)
            {
                BodyRedacted = true;
            }
        }
    }

    public void MarkContentRedacted()
    {
        Body = string.Empty;
        BodyRedacted = true;
    }

    public bool DidNotReachShopper()
    {
        return ProviderStatus is "failed" or "undelivered";
    }
}
