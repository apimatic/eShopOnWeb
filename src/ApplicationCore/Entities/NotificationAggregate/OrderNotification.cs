using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

public class OrderNotification : BaseEntity, IAggregateRoot
{
    public const string UnknownStatus = "unknown";
    public const string FailedStatus = "failed";

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string buyerId,
        OrderNotificationKind kind,
        string destination,
        string body,
        int? contactNumberId = null,
        DateTimeOffset? scheduledSendAt = null,
        int? resendOfNotificationId = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(destination, nameof(destination));
        Guard.Against.Null(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        Destination = destination;
        Body = body;
        ContactNumberId = contactNumberId;
        ScheduledSendAt = scheduledSendAt;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        ProviderStatus = UnknownStatus;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationKind Kind { get; private set; }
    public string Destination { get; private set; }
    public int? ContactNumberId { get; private set; }
    public string Body { get; private set; }
    public bool ContentRedacted { get; private set; }
    public string? ProviderSid { get; private set; }
    public string ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledSendAt { get; private set; }
    public int? ResendOfNotificationId { get; private set; }
    public string? IdempotencyKey { get; private set; }

    public string? BodyForDisplay => ContentRedacted ? null : Body;

    public void ApplyProviderAcceptance(string? sid, string? status, int? errorCode, string? errorMessage)
    {
        ProviderSid = sid;
        ProviderStatus = string.IsNullOrWhiteSpace(status) ? UnknownStatus : status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void MarkSendFailed(string reason)
    {
        ProviderSid = ProviderSid;
        ProviderStatus = FailedStatus;
        ProviderErrorMessage = reason;
    }

    public void RefreshFromProvider(string? status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrWhiteSpace(status))
        {
            ProviderStatus = status;
        }

        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void RedactLocalContent()
    {
        Body = string.Empty;
        ContentRedacted = true;
    }

    public bool DidNotReachShopper()
    {
        return ProviderStatus is not "delivered";
    }

    public bool IsTerminalStatus()
    {
        return ProviderStatus is "delivered" or FailedStatus or "undelivered" or "canceled";
    }
}
