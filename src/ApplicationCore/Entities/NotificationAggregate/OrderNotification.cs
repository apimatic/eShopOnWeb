using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of a single SMS notification sent (or attempted) for an order,
/// carrying the provider's identifier and latest known delivery outcome.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationType notificationType,
        string toNumber,
        string? body,
        string? providerMessageSid,
        string? providerStatus,
        int? providerErrorCode,
        string? providerErrorMessage,
        DateTimeOffset? scheduledFor = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        NotificationType = notificationType;
        ToNumber = toNumber;
        Body = body;
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationType NotificationType { get; private set; }
    public string ToNumber { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFor { get; private set; }
    public bool IsContentDisposed { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public void UpdateProviderOutcome(string? status, int? errorCode, string? errorMessage)
    {
        ProviderStatus = status ?? ProviderStatus;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void DisposeContent()
    {
        Body = null;
        IsContentDisposed = true;
    }
}
