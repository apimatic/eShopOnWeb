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
        string? destination,
        string? body,
        string? providerSid,
        string? providerStatus,
        DateTimeOffset? scheduledAt = null,
        int? sourceNotificationId = null,
        int? errorCode = null,
        string? errorMessage = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(kind, nameof(kind));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Destination = destination;
        Body = body;
        ProviderSid = providerSid;
        ProviderStatus = providerStatus;
        ScheduledAt = scheduledAt;
        SourceNotificationId = sourceNotificationId;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Kind { get; private set; }
    public string? Destination { get; private set; }
    public string? Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    public void ApplyProviderState(string? providerSid, string? providerStatus, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(providerSid))
        {
            ProviderSid = providerSid;
        }

        ProviderStatus = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public override string ToString() => $"{nameof(OrderNotification)} {Id} kind={Kind} status={ProviderStatus}";
}
