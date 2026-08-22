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
        OrderNotificationKind kind,
        string body,
        string destinationNumber,
        string? providerMessageSid,
        string providerStatus,
        string? providerErrorCode = null,
        DateTimeOffset? scheduledSendAt = null,
        int? sourceNotificationId = null,
        string? resendIdempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.NullOrEmpty(providerStatus, nameof(providerStatus));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Body = body ?? string.Empty;
        DestinationNumber = destinationNumber;
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
        ScheduledSendAt = scheduledSendAt;
        SourceNotificationId = sourceNotificationId;
        ResendIdempotencyKey = resendIdempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string Body { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public string? ProviderErrorCode { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public bool ContentRedacted { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? ResendIdempotencyKey { get; private set; }

    public void ApplyProviderState(string status, string? errorCode, string? body)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        if (ContentRedacted)
        {
            Body = string.Empty;
            return;
        }

        if (body != null)
        {
            Body = body;
        }
    }

    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Body = string.Empty;
    }

    public override string ToString() => $"OrderNotification:{Id}";
}
