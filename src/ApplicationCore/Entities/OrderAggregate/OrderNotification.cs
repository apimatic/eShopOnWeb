using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        int? contactNumberId,
        string destinationPhoneNumber,
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? scheduledSendAt = null,
        int? sourceNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationPhoneNumber, nameof(destinationPhoneNumber));
        Guard.Against.Null(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        DestinationPhoneNumber = destinationPhoneNumber;
        Kind = kind;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
        ScheduledSendAt = scheduledSendAt;
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string DestinationPhoneNumber { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string Body { get; private set; }
    public bool ContentDisposed { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void ApplyProviderAcceptance(string sid, string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        ProviderMessageSid = sid;
        ProviderStatus = string.IsNullOrWhiteSpace(status) ? "queued" : status;
        ProviderErrorCode = errorCode;
    }

    public void ApplyProviderSnapshot(string status, int? errorCode, string? bodyFromProvider)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            ProviderStatus = status;
        }

        ProviderErrorCode = errorCode;

        if (!ContentDisposed && bodyFromProvider != null)
        {
            Body = bodyFromProvider;
        }
    }

    public void MarkSendFailed(int? errorCode)
    {
        ProviderStatus = "failed";
        ProviderErrorCode = errorCode;
    }

    public void MarkContentDisposed()
    {
        Body = string.Empty;
        ContentDisposed = true;
    }

    public bool IsScheduledPending()
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

    public static bool IsTerminalStatus(string status) =>
        status is "delivered" or "undelivered" or "failed" or "canceled" or "received" or "read";
}
