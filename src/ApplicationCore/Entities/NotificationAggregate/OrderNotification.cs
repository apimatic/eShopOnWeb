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
        OrderNotificationKind kind,
        string destinationNumber,
        string body,
        DateTimeOffset? scheduledFor = null,
        int? resentFromNotificationId = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.Null(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        Body = body;
        ScheduledFor = scheduledFor;
        ResentFromNotificationId = resentFromNotificationId;
        ProviderStatus = "pending";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? ProviderDateSent { get; private set; }
    public int? ResentFromNotificationId { get; private set; }
    public bool ContentRedacted { get; private set; }

    public void RecordProviderResult(
        string? providerMessageSid,
        string status,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? dateSent,
        string? bodyFromProvider = null)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        if (!string.IsNullOrEmpty(providerMessageSid))
        {
            ProviderMessageSid = providerMessageSid;
        }

        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ProviderDateSent = dateSent;

        if (!ContentRedacted && bodyFromProvider is not null)
        {
            Body = bodyFromProvider;
        }
    }

    public void MarkSendFailed(string reason)
    {
        ProviderStatus = "failed";
        ProviderErrorMessage = reason;
    }

    public void MarkContentRedacted()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }

    public bool IsTerminalStatus()
    {
        return ProviderStatus is "delivered" or "undelivered" or "failed" or "canceled" or "cancelled";
    }
}
