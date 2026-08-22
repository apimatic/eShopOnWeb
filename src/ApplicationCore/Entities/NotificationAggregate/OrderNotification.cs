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
        OrderNotificationKind kind,
        string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        Kind = kind;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public void RecordProviderAcceptance(string sid, string status, DateTimeOffset? sendAt)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderMessageSid = sid;
        ProviderStatus = status;
        ScheduledSendAt = sendAt;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void RecordProviderFailure(string? status, int? errorCode)
    {
        ProviderStatus = string.IsNullOrEmpty(status) ? "failed" : status;
        ProviderErrorCode = errorCode;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void SyncFromProvider(string status, int? errorCode, string? body)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderStatus = status;
        ProviderErrorCode = errorCode;

        if (ContentRedacted)
        {
            Body = null;
        }
        else if (body is { Length: 0 })
        {
            MarkContentRedacted();
        }
        else if (body is not null)
        {
            Body = body;
        }

        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
    }

    public void ClearContactNumber()
    {
        ContactNumberId = null;
    }

    public bool HasTerminalProviderStatus =>
        ProviderStatus is "delivered" or "undelivered" or "failed" or "canceled" or "read";

    public bool DidNotReachShopper =>
        ProviderStatus is "failed" or "undelivered" ||
        (ProviderMessageSid is null && ProviderStatus is not "scheduled");

    public bool IsScheduledFollowUp =>
        Kind == OrderNotificationKind.DeliveryFollowUp &&
        ProviderStatus == "scheduled" &&
        !string.IsNullOrEmpty(ProviderMessageSid);
}
