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
        OrderNotificationKind kind,
        string body,
        DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.Null(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        DestinationNumber = destinationNumber;
        Kind = kind;
        Body = body;
        ScheduledSendAt = scheduledSendAt;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string DestinationNumber { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? LocalSendFailure { get; private set; }

    public bool HasProviderIdentity => !string.IsNullOrWhiteSpace(ProviderMessageSid);

    public void RecordProviderAcceptance(string sid, string? status, DateTimeOffset? dateSent)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        ProviderMessageSid = sid;
        ProviderStatus = status;
        ProviderDateSent = dateSent;
        LocalSendFailure = null;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void RecordLocalSendFailure(string reason)
    {
        LocalSendFailure = reason;
        ProviderStatus = "send_failed";
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void SyncFromProvider(string? status, int? errorCode, string? errorMessage, DateTimeOffset? dateSent, string? body)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            ProviderStatus = status;
        }

        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (dateSent.HasValue)
        {
            ProviderDateSent = dateSent;
        }

        if (ContentRedacted)
        {
            Body = string.Empty;
        }
        else if (body is not null)
        {
            Body = body;
            if (body.Length == 0)
            {
                ContentRedacted = true;
            }
        }

        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Body = string.Empty;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public bool WasNotDelivered()
    {
        if (!string.IsNullOrWhiteSpace(LocalSendFailure))
        {
            return true;
        }

        return ProviderStatus is "failed" or "undelivered" or "canceled" or "send_failed";
    }
}
