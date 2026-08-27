using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Record of a single message sent (or attempted) to a shopper about an order.
/// Carries the provider's identifier and latest known delivery outcome so later
/// requests can act on the message (cancel, resend, redact, reconcile).
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, NotificationKind kind, string toNumber,
        string? body, DateTimeOffset? scheduledFor = null, string? idempotencyKey = null,
        int? originalNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        OriginalNotificationId = originalNotificationId;
        Status = NotificationStatuses.Pending;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public NotificationKind Kind { get; private set; }
    public string ToNumber { get; private set; }
    public string? Body { get; private set; }
    public string? MessageSid { get; private set; }
    public string Status { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ScheduledFor { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public int? OriginalNotificationId { get; private set; }
    public bool ContentRedacted { get; private set; }

    public void MarkAccepted(string messageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        MessageSid = messageSid;
        Status = providerStatus;
    }

    public void MarkFailed(string status, int? providerErrorCode, string? providerErrorMessage)
    {
        Status = status;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
    }

    public void UpdateFromProvider(string providerStatus, int? providerErrorCode, string? providerErrorMessage)
    {
        Status = providerStatus;
        ProviderErrorCode = providerErrorCode;
        ProviderErrorMessage = providerErrorMessage;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}

public static class NotificationStatuses
{
    public const string Pending = "pending";
    public const string Failed = "failed";
}
