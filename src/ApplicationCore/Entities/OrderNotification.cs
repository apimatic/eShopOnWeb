using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        string kind,
        string destinationNumber,
        string? body,
        string? providerMessageSid,
        string providerStatus,
        int? providerErrorCode,
        string? providerErrorMessage,
        DateTimeOffset? scheduledAt,
        int? sourceNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(kind, nameof(kind));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.NullOrEmpty(providerStatus, nameof(providerStatus));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        Body = body;
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
        ScheduledAt = scheduledAt;
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Kind { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void ApplyProviderState(
        string status,
        string? body,
        int? errorCode,
        string? errorMessage,
        string? providerMessageSid)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (!string.IsNullOrWhiteSpace(providerMessageSid))
        {
            ProviderMessageSid = providerMessageSid;
        }

        if (ContentRedacted)
        {
            Body = null;
        }
        else if (body != null)
        {
            Body = body;
        }
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
    }
}
