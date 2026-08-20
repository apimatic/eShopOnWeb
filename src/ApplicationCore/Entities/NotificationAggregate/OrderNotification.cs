using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered",
        "undelivered",
        "failed",
        "canceled",
        "received",
        "read"
    };

    private static readonly HashSet<string> CancellableStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "scheduled",
        "accepted",
        "queued",
        "pending"
    };

    private static readonly HashSet<string> ReachedShopperStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered",
        "read"
    };

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string destinationNumber,
        string body,
        int? contactNumberId,
        int? parentNotificationId = null,
        string? idempotencyKey = null,
        DateTimeOffset? scheduledFor = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        Body = body;
        ContactNumberId = contactNumberId;
        ParentNotificationId = parentNotificationId;
        IdempotencyKey = idempotencyKey;
        ScheduledFor = scheduledFor;
        ProviderStatus = "pending";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string DestinationNumber { get; private set; }
    public string Body { get; private set; }
    public bool ContentDisposed { get; private set; }
    public int? ContactNumberId { get; private set; }
    public int? ParentNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string? ProviderMessageSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset? LastSyncedAt { get; private set; }

    public bool IsTerminal => TerminalStatuses.Contains(ProviderStatus);

    public bool IsCancellableFollowUp =>
        Kind == OrderNotificationKind.DeliveryFollowUp
        && !string.IsNullOrEmpty(ProviderMessageSid)
        && CancellableStatuses.Contains(ProviderStatus);

    public bool DidNotReachShopper =>
        !ReachedShopperStatuses.Contains(ProviderStatus);

    public void RecordProviderAccepted(string sid, string status)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderMessageSid = sid;
        ProviderStatus = status;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void RecordLocalSendFailure(string errorMessage)
    {
        ProviderStatus = "failed";
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void ApplyProviderState(string status, int? errorCode, string? errorMessage, string? body)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        LastSyncedAt = DateTimeOffset.UtcNow;

        if (ContentDisposed)
        {
            Body = string.Empty;
            return;
        }

        if (body is not null)
        {
            Body = body;
            if (body.Length == 0)
            {
                ContentDisposed = true;
            }
        }
    }

    public void MarkContentDisposed()
    {
        ContentDisposed = true;
        Body = string.Empty;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public string ResolveResendBody()
    {
        if (!string.IsNullOrEmpty(Body))
        {
            return Body;
        }

        return Kind switch
        {
            OrderNotificationKind.OrderPlaced => $"eShopOnWeb: Your order #{OrderId} has been placed.",
            OrderNotificationKind.OrderDispatched => $"eShopOnWeb: Your order #{OrderId} is on its way.",
            OrderNotificationKind.DeliveryFollowUp => $"eShopOnWeb: How did the delivery of order #{OrderId} go?",
            OrderNotificationKind.OrderCancelled => $"eShopOnWeb: Your order #{OrderId} has been cancelled.",
            _ => $"eShopOnWeb: Update for order #{OrderId}."
        };
    }
}
