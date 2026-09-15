using System;
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
        NotificationKind kind,
        string destinationNumber,
        int? contactNumberId,
        string? body,
        DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        ContactNumberId = contactNumberId;
        Body = body;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? SubmitError { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void RecordProviderAcceptance(string sid, string status, DateTimeOffset? dateSent, int? errorCode)
    {
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderDateSent = dateSent;
        ProviderErrorCode = errorCode;
        SubmitError = null;
    }

    public void RecordSubmitFailure(string error)
    {
        SubmitError = error;
        ProviderStatus = "submit_failed";
    }

    public void ApplyProviderState(string status, DateTimeOffset? dateSent, int? errorCode, string? bodyIfPresent, bool contentAlreadyRedacted)
    {
        ProviderStatus = status;
        ProviderDateSent = dateSent;
        ProviderErrorCode = errorCode;
        if (contentAlreadyRedacted)
        {
            return;
        }

        if (bodyIfPresent != null)
        {
            Body = bodyIfPresent;
        }
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool IsTerminalProviderStatus()
    {
        return ProviderStatus is "delivered" or "undelivered" or "failed" or "canceled" or "read";
    }

    public bool DidNotReachShopper()
    {
        if (string.IsNullOrEmpty(ProviderMessageSid))
        {
            return true;
        }

        return ProviderStatus is "failed" or "undelivered" or "canceled" or "submit_failed";
    }
}
