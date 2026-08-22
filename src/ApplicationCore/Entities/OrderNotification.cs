using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        string kind,
        string destinationNumber,
        string? body,
        string? providerSid,
        string? providerStatus,
        int? errorCode,
        string? errorMessage,
        DateTimeOffset? sendAt = null,
        int? sourceNotificationId = null,
        string? idempotencyKey = null,
        string? sendFailure = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(kind, nameof(kind));
        Guard.Against.NullOrEmpty(destinationNumber, nameof(destinationNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        DestinationNumber = destinationNumber;
        Body = body;
        ProviderSid = providerSid;
        ProviderStatus = providerStatus;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        SendAt = sendAt;
        SourceNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
        SendFailure = sendFailure;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string Kind { get; private set; }
    public string DestinationNumber { get; private set; }
    public string? Body { get; private set; }
    public string? ProviderSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? SendAt { get; private set; }
    public int? SourceNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public bool ContentDisposed { get; private set; }
    public string? SendFailure { get; private set; }

    public void ApplyProviderState(string? sid, string? status, int? errorCode, string? errorMessage, string? body)
    {
        if (!string.IsNullOrEmpty(sid))
        {
            ProviderSid = sid;
        }

        ProviderStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        SendFailure = null;

        if (!ContentDisposed && body is not null)
        {
            Body = body;
        }
    }

    public void MarkSendFailed(string reason)
    {
        Guard.Against.NullOrEmpty(reason, nameof(reason));
        SendFailure = reason;
    }

    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }

    public bool IsStillPendingSend()
    {
        if (string.IsNullOrEmpty(ProviderSid))
        {
            return false;
        }

        return string.Equals(ProviderStatus, NotificationStatuses.Scheduled, StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, NotificationStatuses.Queued, StringComparison.OrdinalIgnoreCase)
            || string.Equals(ProviderStatus, NotificationStatuses.Accepted, StringComparison.OrdinalIgnoreCase);
    }
}

public static class NotificationKinds
{
    public const string OrderPlaced = "OrderPlaced";
    public const string OrderDispatched = "OrderDispatched";
    public const string DeliveryFollowUp = "DeliveryFollowUp";
    public const string OrderCancelled = "OrderCancelled";
    public const string Resend = "Resend";
}

public static class NotificationStatuses
{
    public const string Scheduled = "scheduled";
    public const string Queued = "queued";
    public const string Accepted = "accepted";
    public const string Canceled = "canceled";
    public const string Failed = "failed";
    public const string Undelivered = "undelivered";
    public const string Delivered = "delivered";
    public const string Sent = "sent";
}
