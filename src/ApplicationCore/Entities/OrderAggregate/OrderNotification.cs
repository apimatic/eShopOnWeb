using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        int? contactNumberId,
        string destinationE164,
        string kind,
        string body)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationE164, nameof(destinationE164));
        Guard.Against.NullOrEmpty(kind, nameof(kind));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        DestinationE164 = destinationE164;
        Kind = kind;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string DestinationE164 { get; private set; }
    public string Kind { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ErrorCode { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void RecordProviderAcceptance(string providerMessageSid, string status, DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderMessageSid = providerMessageSid;
        ProviderStatus = status;
        ScheduledSendAt = scheduledSendAt;
        ErrorCode = null;
    }

    public void RecordProviderFailure(string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ErrorCode = errorCode;
    }

    public void ApplyProviderSnapshot(string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ErrorCode = errorCode;
    }

    public void MarkResendOf(int originalNotificationId)
    {
        Guard.Against.OutOfRange(originalNotificationId, nameof(originalNotificationId), 1, int.MaxValue);
        OriginalNotificationId = originalNotificationId;
    }

    public void RedactContent()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }

    public bool IsPendingFollowUp()
    {
        if (Kind != OrderNotificationKind.DeliveryFollowUp)
        {
            return false;
        }

        if (string.IsNullOrEmpty(ProviderMessageSid))
        {
            return false;
        }

        return ProviderStatus is "scheduled" or "accepted" or "queued";
    }
}
