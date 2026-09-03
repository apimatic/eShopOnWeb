using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618
    private OrderNotification() { }
    #pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationKind kind,
        string? destinationNumber,
        string? body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
        ProviderStatus = "not_sent";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? DestinationNumber { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProviderDateCreated { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? ResendIdempotencyKey { get; private set; }

    public void RecordProviderAcceptance(
        string sid,
        string status,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateSent)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderSid = sid;
        ApplyProviderState(status, errorCode, errorMessage, dateCreated, dateSent);
    }

    public void RecordSendFailure(string status, int? errorCode, string? errorMessage)
    {
        ProviderSid = null;
        ApplyProviderState(status, errorCode, errorMessage, null, null);
    }

    public void ApplyProviderState(
        string status,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? dateCreated,
        DateTimeOffset? dateSent)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (dateCreated.HasValue)
        {
            ProviderDateCreated = dateCreated;
        }
        if (dateSent.HasValue)
        {
            ProviderDateSent = dateSent;
        }
    }

    public void MarkResend(int originalNotificationId, string idempotencyKey)
    {
        Guard.Against.NegativeOrZero(originalNotificationId, nameof(originalNotificationId));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        ResendOfNotificationId = originalNotificationId;
        ResendIdempotencyKey = idempotencyKey;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool IsPendingFollowUp()
    {
        if (Kind != NotificationKind.DeliveryFollowUp || string.IsNullOrEmpty(ProviderSid) || ContentRedacted)
        {
            return false;
        }

        return ProviderStatus is "scheduled" or "accepted" or "queued";
    }
}
