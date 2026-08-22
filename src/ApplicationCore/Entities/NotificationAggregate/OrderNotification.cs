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
        string destinationNumber,
        string? providerSid,
        string? providerStatus,
        int? providerErrorCode,
        string? providerErrorMessage,
        DateTimeOffset? scheduledSendAt)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body;
        DestinationNumber = destinationNumber;
        ProviderSid = providerSid;
        ProviderStatus = providerStatus ?? "not_sent";
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
        ScheduledSendAt = scheduledSendAt;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string? Body { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? ProviderSid { get; private set; }
    public string ProviderStatus { get; private set; } = "not_sent";
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public bool ContentRedacted { get; private set; }

    public void ApplyProviderState(string? providerSid, string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(providerSid))
        {
            ProviderSid = providerSid;
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            ProviderStatus = status;
        }

        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
    }

    public bool IsPendingWithProvider()
    {
        return ProviderStatus is "scheduled" or "accepted" or "queued" or "sending";
    }
}
